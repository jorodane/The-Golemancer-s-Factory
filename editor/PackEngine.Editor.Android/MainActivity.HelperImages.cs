using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Widget;
using PackEngine.Workspace;
using Path = System.IO.Path;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private string pickingHelper = "";
    private void PickHelperImage(AiHelper helper)
    {
        pickingHelper = helper.Id;
#pragma warning disable CA1422, CS0618
        StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("image/*").AddCategory(Intent.CategoryOpenable), 3);
#pragma warning restore CA1422, CS0618
    }
    private void ReadHelperImage(global::Android.Net.Uri uri)
    {
        string id = pickingHelper; pickingHelper = "";
        try
        {
            var helper = mobileDirectory.Helpers.Single(h => h.Id == id);
            using var source = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("이미지를 열지 못했어.");
            using var memory = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            { if (memory.Length + count > 12 * 1024 * 1024) throw new InvalidDataException("12 MiB 이하 이미지를 골라줘."); memory.Write(buffer, 0, count); }
            byte[] bytes = memory.ToArray(); using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            using var ignored = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) throw new InvalidDataException("지원하는 이미지 파일을 골라줘.");
            int sample = 1; while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 1024) sample *= 2;
            using var options = new BitmapFactory.Options { InSampleSize = sample };
            var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options) ?? throw new InvalidDataException("이미지를 읽지 못했어.");
            var preview = new ImageView(this); preview.SetImageBitmap(bitmap); preview.SetAdjustViewBounds(true);
            var dialog = new AlertDialog.Builder(this).SetTitle(helper.Name + " · 이미지 미리보기")!.SetView(preview)!
                .SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("이 이미지 사용", (_, _) =>
                {
                    try
                    {
                        using var output = new MemoryStream();
                        if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output)) throw new IOException("이미지를 저장하지 못했어.");
                        string path = Path.Combine(root, "Helpers", helper.Id, "avatar.png"); AtomicWrite(path, output.ToArray());
                        helper.AvatarPath = path; SaveMobileDirectory(); RefreshMobileManagement(); Report(helper.Name + "의 이미지를 바꿨어.");
                    }
                    catch (Exception e) { Report(e.Message); }
                })!.Create()!;
            dialog.DismissEvent += (_, _) => { preview.SetImageDrawable(null); bitmap.Dispose(); }; dialog.Show();
        }
        catch (Exception e) { Report(e.Message); }
    }
    private void AddHelperImage(LinearLayout panel, AiHelper helper)
    {
        panel.AddView(MobileAiCircle(helper.Name, helper.AvatarPath, () => { }, main: MobileProject && helper.Id == mobileProjectStudio.MainHelperId, size: 64));
        panel.AddView(AiAction("이미지 선택·변경", () => PickHelperImage(helper)));
        if (helper.AvatarPath.Length > 0) panel.AddView(AiAction("이미지 제거", () => { helper.AvatarPath = ""; SaveMobileDirectory(); RefreshMobileManagement(); Report("도우미 이미지를 제거했어."); }));
    }
}
