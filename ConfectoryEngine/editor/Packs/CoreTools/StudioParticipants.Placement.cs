using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed partial class StudioParticipants
{
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static void Dimensions(params double[] values)
    {
        if (values.Any(value => !Finite(value) || value < 0)) throw new ArgumentException("화면과 캐릭터 크기는 유한한 양수 또는 0이어야 해.");
    }
    internal static EditorStudioPlacement Fit(double x, double y, double width, double height, double characterWidth, double characterHeight)
    {
        Dimensions(width, height, characterWidth, characterHeight);
        if (!Finite(x) || !Finite(y)) throw new ArgumentException("이동 위치는 유한한 값이어야 해.");
        double scale = Math.Min(1, Math.Max(.5, Math.Min((height - 18) / Math.Max(1, characterHeight), (width - 18) / Math.Max(1, characterWidth))));
        return new(Math.Max(0, Math.Min(x, width - characterWidth * scale)), Math.Max(0, Math.Min(y, height - characterHeight * scale)), scale);
    }
    public EditorStudioPlacement Layout(string participantId, double width, double height, double characterWidth, double characterHeight)
    {
        Dimensions(width, height, characterWidth, characterHeight);
        _ = collaboration.Require(actor, ParticipantPermission.None);
        var participant = collaboration.Require(participantId, ParticipantPermission.None);
        var placement = collaboration.View(actor, participantId);
        double Coordinate(double? value, double fallback) => value is { } number && Finite(number) ? number : Finite(fallback) ? fallback : 0;
        var fitted = Fit(Coordinate(placement.X, participant.X), Coordinate(placement.Y, participant.Y), width, height, characterWidth, characterHeight);
        placement.X = fitted.X; placement.Y = fitted.Y;
        return fitted;
    }
    public EditorStudioPlacement Move(string participantId, double x, double y, double width, double height, double characterWidth, double characterHeight, bool persist = false)
    {
        Dimensions(width, height, characterWidth, characterHeight);
        if (!Finite(x) || !Finite(y)) throw new ArgumentException("이동 위치는 유한한 값이어야 해.");
        _ = collaboration.Require(actor, ParticipantPermission.None);
        _ = collaboration.Require(participantId, ParticipantPermission.None);
        var existing = collaboration.State.Views.FirstOrDefault(v => v.Viewer == actor && v.ParticipantId == participantId);
        var view = collaboration.View(actor, participantId); double? oldX = view.X, oldY = view.Y;
        view.X = x; view.Y = y;
        var result = Layout(participantId, width, height, characterWidth, characterHeight);
        if (persist) try { collaboration.Save(); } catch (Exception failure)
        {
            view.X = oldX; view.Y = oldY;
            if (existing is null) collaboration.State.Views.Remove(view);
            RestoreSavedView(failure);
            throw;
        }
        return result;
    }
    public void Display(string participantId, CharacterDisplay display)
    {
        if (!Enum.IsDefined(typeof(CharacterDisplay), display)) throw new ArgumentException("지원하지 않는 캐릭터 표시 상태야.");
        _ = collaboration.Require(actor, ParticipantPermission.None);
        _ = collaboration.Require(participantId, ParticipantPermission.None);
        var existing = collaboration.State.Views.FirstOrDefault(v => v.Viewer == actor && v.ParticipantId == participantId);
        var view = collaboration.View(actor, participantId); var previous = view.Display;
        try { view.Display = display; collaboration.Save(); }
        catch (Exception failure) { view.Display = previous; if (existing is null) collaboration.State.Views.Remove(view); RestoreSavedView(failure); throw; }
    }
    private void RestoreSavedView(Exception failure)
    {
        // Save may already have written bytes before a Changed observer failed.
        try { collaboration.Save(); }
        catch (Exception compensation) { throw new AggregateException("배치 저장과 복원에 실패했어. 다시 열고 확인해줘.", failure, compensation); }
    }
    public void CommitPlacement(string participantId)
    {
        _ = collaboration.Require(actor, ParticipantPermission.None);
        _ = collaboration.Require(participantId, ParticipantPermission.None);
        var view = collaboration.View(actor, participantId);
        if (view.X is { } x && !Finite(x) || view.Y is { } y && !Finite(y)) throw new ArgumentException("이동 위치는 유한한 값이어야 해.");
        // Failed persistence leaves the user's placement draft local for an explicit retry.
        collaboration.Save();
    }
}
