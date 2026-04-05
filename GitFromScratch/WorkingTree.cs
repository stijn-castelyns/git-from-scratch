using System.Text;

namespace GitFromScratch;

internal class WorkingTree
{
    public static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }
}
