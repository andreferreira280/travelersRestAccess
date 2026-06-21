using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Extracts screen-reader-readable text from a UI GameObject.
    /// Travellers Rest uses TextMeshPro exclusively for text (no legacy Text). Reads
    /// TMP_Text (the common base of TextMeshProUGUI and world-space TextMeshPro) so a
    /// floating world-space feedback popup isn't missed just for not being a UI element.
    /// </summary>
    public static class UITextExtractor
    {
        public static string GetReadableText(GameObject go)
        {
            if (go == null) return null;

            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null && !string.IsNullOrWhiteSpace(label.text))
            {
                return CleanText(label.text);
            }

            // No own text (e.g. icon-only buttons, bare sliders). We previously tried
            // guessing a nearby sibling label, but that picked up unrelated text (e.g. the
            // active tab's own label) because this UI doesn't group rows into per-control
            // containers. A humanized version of the internal GameObject name is less
            // pretty but never wrong.
            return Humanize(go.name);
        }

        public static string GetReadableText(TMP_Text label)
        {
            if (label == null || string.IsNullOrWhiteSpace(label.text)) return null;
            return CleanText(label.text);
        }

        private static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            string spaced = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
            spaced = Regex.Replace(spaced, "(?<=[A-Z])(?=[A-Z][a-z])", " ");
            return spaced.Trim();
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Strip TextMeshPro rich-text tags like <b>, <color=...>
            text = Regex.Replace(text, "<.*?>", "");
            return text.Trim();
        }
    }
}
