namespace PowerForge.Web;

/// <summary>Locates URL tokens in source sets without selecting a responsive image or rewriting descriptors.</summary>
internal static class WebSourceSetUrls
{
    internal static IEnumerable<(int Start, int Length)> Ranges(string sourceSet)
    {
        int index = 0;
        while (index < sourceSet.Length)
        {
            while (index < sourceSet.Length && (char.IsWhiteSpace(sourceSet[index]) || sourceSet[index] == ',')) index++;
            if (index >= sourceSet.Length) yield break;
            int start = index;
            while (index < sourceSet.Length && !char.IsWhiteSpace(sourceSet[index])) index++;
            int end = index;
            while (end > start && sourceSet[end - 1] == ',') end--;
            if (end > start) yield return (start, end - start);
            if (end < index) continue;
            int parentheses = 0;
            while (index < sourceSet.Length)
            {
                char current = sourceSet[index++];
                if (current == '(') parentheses++;
                else if (current == ')' && parentheses > 0) parentheses--;
                else if (current == ',' && parentheses == 0) break;
            }
        }
    }
}
