namespace Confectory.EditorPacks;
internal static class EditorPackNames
{
    public static void Check(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_.-]*\z")) throw new InvalidDataException("Invalid editor pack identifier: " + value);
    }
}
