namespace Kavita.Services.Helpers;

public static partial class TitleSortHelper
{
    private const int HangulBase = 0xAC00;
    private const int HangulEnd = 0xD7A3;
    private const int HangulSyllablesPerInitial = 21 * 28;

    private static readonly char[] KoreanInitials =
    [
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
        'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'
    ];

    /// <summary>
    /// Creates the value used for title sorting and jumpbar indexing.
    /// </summary>
    /// <remarks>
    /// Korean Hangul syllables sort under their compatibility choseong so titles like 앨리스 index under ㅇ.
    /// </remarks>
    public static string GetSortTitle(string title, bool removePrefix)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;

        var sortTitle = removePrefix ? BookSortTitlePrefixHelper.GetSortTitle(title) : title;

        return ApplyKoreanInitialSort(sortTitle);
    }

    public static string ApplyKoreanInitialSort(string title)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;

        var indexableTitle = title.TrimStart();
        while (!string.IsNullOrEmpty(indexableTitle) && !char.IsLetterOrDigit(indexableTitle[0]))
        {
            var closingWrapper = GetClosingWrapper(indexableTitle[0]);
            if (closingWrapper != null && indexableTitle[^1] == closingWrapper)
            {
                indexableTitle = indexableTitle[..^1].TrimEnd();
            }

            indexableTitle = indexableTitle[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(indexableTitle)) return title;

        var firstChar = indexableTitle[0];
        if (!IsHangulSyllable(firstChar)) return title;

        var initialIndex = (firstChar - HangulBase) / HangulSyllablesPerInitial;

        return KoreanInitials[initialIndex] + indexableTitle;
    }

    private static bool IsHangulSyllable(char c)
    {
        return c is >= (char)HangulBase and <= (char)HangulEnd;
    }

    private static char? GetClosingWrapper(char c)
    {
        return c switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '<' => '>',
            '「' => '」',
            '『' => '』',
            '《' => '》',
            '〈' => '〉',
            '“' => '”',
            '‘' => '’',
            '"' => '"',
            '\'' => '\'',
            _ => null
        };
    }
}
