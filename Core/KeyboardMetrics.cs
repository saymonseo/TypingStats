namespace TypingStats.Core;

public enum TrackingMode { Text = 0, Keys = 1, Both = 2 }
public sealed record KeyMetricRow(BucketKey Key, string Code, string Label, long Presses, long Repeats, long Injected);

public static class KeyIdentity
{
    public static string Code(uint scan, bool extended, uint vk) => scan == 0 || vk == 0xe7 ? $"vk:{vk:X2}" : $"sc:{(extended ? 0xe000u : 0u) | (scan & 0xff):X4}";
    private static readonly Dictionary<int, string> Labels = new()
    {
        [0x01]="Esc", [0x0e]="Backspace", [0x0f]="Tab", [0x1c]="Enter", [0x1d]="Left Ctrl", [0x2a]="Left Shift", [0x36]="Right Shift", [0x38]="Left Alt", [0x39]="Space", [0x3a]="Caps Lock",
        [0xe01d]="Right Ctrl", [0xe038]="Right Alt", [0xe01c]="Num Enter", [0xe05b]="Left Win", [0xe05c]="Right Win", [0xe05d]="Menu",
        [0xe047]="Home", [0xe048]="Up", [0xe049]="Page Up", [0xe04b]="Left", [0xe04d]="Right", [0xe04f]="End", [0xe050]="Down", [0xe051]="Page Down", [0xe052]="Insert", [0xe053]="Delete",
        [0xe037]="Print Screen", [0x46]="Scroll Lock", [0x45]="Pause", [0xe045]="Num Lock", [0xe035]="Num /", [0x37]="Num *", [0x4a]="Num -", [0x4e]="Num +",
        [0x47]="Num 7", [0x48]="Num 8", [0x49]="Num 9", [0x4b]="Num 4", [0x4c]="Num 5", [0x4d]="Num 6", [0x4f]="Num 1", [0x50]="Num 2", [0x51]="Num 3", [0x52]="Num 0", [0x53]="Num .",
        [0x29]="` / Ё", [0x0c]="-", [0x0d]="=", [0x1a]="[ / Х", [0x1b]="] / Ъ", [0x2b]="\\", [0x27]="; / Ж", [0x28]="' / Э", [0x33]=", / Б", [0x34]=". / Ю", [0x35]="/", [0x57]="F11", [0x58]="F12"
    };
    static KeyIdentity()
    {
        for (var i = 0; i < 10; i++) Labels[0x02 + i] = ((i + 1) % 10).ToString();
        for (var i = 0; i < 10; i++) Labels[0x3b + i] = "F" + (i + 1);
        var rows = new[] { (0x10, "QWERTYUIOP", "ЙЦУКЕНГШЩЗ"), (0x1e, "ASDFGHJKL", "ФЫВАПРОЛД"), (0x2c, "ZXCVBNM", "ЯЧСМИТЬ") };
        foreach (var (start, en, ru) in rows) for (var i = 0; i < en.Length; i++) Labels[start + i] = en[i] + " / " + ru[i];
    }
    public static string Label(uint scan, bool extended, uint vk)
    {
        if (vk == 0xe7) return "Unicode-ввод (программный)";
        var number = (extended ? 0xe000 : 0) | (int)(scan & 0xff);
        return scan != 0 && Labels.TryGetValue(number, out var label) ? label : $"VK {vk:X2} / scan {number:X4}";
    }
    public static bool CountPress(TrackingMode mode, bool repeat, bool injected, bool countRepeats, bool countInjected) =>
        mode != TrackingMode.Text && (!repeat || countRepeats) && (!injected || countInjected);
}
