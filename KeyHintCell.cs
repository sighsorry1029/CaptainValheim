using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal sealed class KeyHintCell
{
    private readonly List<TMP_Text> _keys = [];
    private readonly List<GameObject> _keyParents = [];
    private readonly List<TMP_Text> _extraTexts = [];
    private readonly List<TMP_Text> _generatedSeparatorTexts = [];
    private TMP_Text? _label;

    private KeyHintCell(GameObject root)
    {
        Root = root;
        RefreshChildren();
    }

    internal GameObject Root { get; }

    internal static KeyHintCell? CloneFrom(GameObject? template, string name)
    {
        if (!IsUsableTemplate(template) || template!.transform.parent == null)
        {
            return null;
        }

        GameObject clone = Object.Instantiate(template, template.transform.parent, false);
        clone.name = name;
        clone.SetActive(false);
        return new KeyHintCell(clone);
    }

    internal static bool IsUsableTemplate(GameObject? template)
    {
        return template != null &&
               template.transform.parent != null &&
               !template.name.StartsWith("CaptainValheim_") &&
               template.GetComponentsInChildren<TMP_Text>(includeInactive: true).Length > 0;
    }

    internal static Transform? FindParentWithTemplates(GameObject root, string name)
    {
        Transform transform = root.transform.Find(name);
        if (transform == null)
        {
            return null;
        }

        return transform
            .Cast<Transform>()
            .Any(static child => IsUsableTemplate(child.gameObject))
            ? transform
            : null;
    }

    internal void Set(string label, IReadOnlyList<string> keys, float preferredTextWidth = 0f, bool hideExtraTexts = false)
    {
        EnsureKeyCount(keys.Count);
        Root.SetActive(true);

        if (_label != null)
        {
            SetText(_label, label);
            if (preferredTextWidth > 0f && _label.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.preferredWidth = preferredTextWidth;
            }
        }

        for (int i = 0; i < _keys.Count; i++)
        {
            bool show = i < keys.Count;
            if (i < _keyParents.Count && _keyParents[i] != null)
            {
                _keyParents[i].SetActive(show);
            }

            if (show)
            {
                SetText(_keys[i], keys[i]);
            }
        }

        if (hideExtraTexts)
        {
            foreach (TMP_Text extraText in _extraTexts)
            {
                if (extraText != null)
                {
                    extraText.gameObject.SetActive(false);
                }
            }
        }
        else
        {
            EnsureSeparatorCount(Mathf.Max(0, keys.Count - 1));
            for (int i = 0; i < _generatedSeparatorTexts.Count; i++)
            {
                TMP_Text separator = _generatedSeparatorTexts[i];
                if (separator == null)
                {
                    continue;
                }

                bool show = i < keys.Count - 1;
                separator.gameObject.SetActive(show);
                if (show)
                {
                    SetText(separator, "+");
                }
            }

            foreach (TMP_Text extraText in _extraTexts)
            {
                if (extraText != null)
                {
                    extraText.gameObject.SetActive(keys.Count > 1);
                }
            }
        }
    }

    internal void SetActive(bool active)
    {
        if (Root != null)
        {
            Root.SetActive(active);
        }
    }

    internal void MoveBefore(GameObject? template)
    {
        if (template == null ||
            Root == null ||
            Root == template ||
            Root.transform.parent != template.transform.parent)
        {
            return;
        }

        int currentIndex = Root.transform.GetSiblingIndex();
        int templateIndex = template.transform.GetSiblingIndex();
        int targetIndex = currentIndex < templateIndex ? templateIndex - 1 : templateIndex;
        if (currentIndex != targetIndex)
        {
            Root.transform.SetSiblingIndex(Mathf.Max(0, targetIndex));
        }
    }

    internal void RebuildParentLayout()
    {
        if (Root != null && Root.transform.parent is RectTransform parent)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }
    }

    private void EnsureKeyCount(int count)
    {
        RefreshChildren();
        if (count <= _keys.Count || _keyParents.Count == 0)
        {
            return;
        }

        GameObject template = _keyParents[0];
        Transform parent = template.transform.parent;
        while (_keys.Count < count)
        {
            GameObject clone = Object.Instantiate(template, parent, false);
            clone.name = _keys.Count == 1 ? "key_bkg (1)" : $"key_bkg ({_keys.Count})";
            RefreshChildren();
            if (_keys.Count == 0)
            {
                break;
            }
        }
    }

    private void EnsureSeparatorCount(int count)
    {
        if (count <= 0 || _keyParents.Count == 0 || _extraTexts.Count > 0)
        {
            return;
        }

        Transform? parent = _keyParents[0].transform.parent;
        TMP_Text? templateText = _label ?? _keys.FirstOrDefault();
        if (parent == null || templateText == null)
        {
            return;
        }

        while (_generatedSeparatorTexts.Count < count)
        {
            GameObject separatorObject = new($"CaptainValheim_KeyHintSeparator_{_generatedSeparatorTexts.Count}");
            separatorObject.transform.SetParent(parent, false);
            RectTransform rectTransform = separatorObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(18f, 24f);

            TextMeshProUGUI separator = separatorObject.AddComponent<TextMeshProUGUI>();
            separator.font = templateText.font;
            separator.fontSharedMaterial = templateText.fontSharedMaterial;
            separator.fontSize = templateText.fontSize;
            separator.fontStyle = templateText.fontStyle;
            separator.color = templateText.color;
            separator.alignment = TextAlignmentOptions.Center;
            separator.raycastTarget = false;
            separator.text = "+";

            LayoutElement layoutElement = separatorObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 18f;
            layoutElement.minWidth = 12f;

            _generatedSeparatorTexts.Add(separator);
        }

        for (int i = 0; i < _generatedSeparatorTexts.Count; i++)
        {
            TMP_Text separator = _generatedSeparatorTexts[i];
            if (separator == null)
            {
                continue;
            }

            int targetKeyIndex = Mathf.Min(i + 1, _keyParents.Count - 1);
            if (targetKeyIndex >= 0 && targetKeyIndex < _keyParents.Count && _keyParents[targetKeyIndex] != null)
            {
                separator.transform.SetSiblingIndex(_keyParents[targetKeyIndex].transform.GetSiblingIndex());
            }
        }
    }

    private void RefreshChildren()
    {
        _keys.Clear();
        _keyParents.Clear();
        _extraTexts.Clear();
        _label = null;

        TMP_Text[] texts = Root
            .GetComponentsInChildren<TMP_Text>(includeInactive: true)
            .Where(static text => text != null)
            .ToArray();
        foreach (TMP_Text text in texts)
        {
            Localization.instance?.RemoveTextFromCache(text);
            if (text is TextMeshProUGUI textMesh)
            {
                textMesh.raycastTarget = false;
            }
        }

        _keys.AddRange(texts.Where(static text => string.Equals(text.name, "Key", StringComparison.OrdinalIgnoreCase)));
        if (_keys.Count == 0)
        {
            TMP_Text? inferredKey = texts.FirstOrDefault(static text => LooksLikeKeyBindingText(text.text))
                                   ?? texts.OrderBy(static text => text.transform.position.x).LastOrDefault();
            if (inferredKey != null && texts.Length > 1)
            {
                _keys.Add(inferredKey);
            }
        }

        _label = texts.FirstOrDefault(text => string.Equals(text.name, "Text", StringComparison.OrdinalIgnoreCase) &&
                                              !_keys.Contains(text))
                 ?? texts.FirstOrDefault(text => !_keys.Contains(text) && !LooksLikeKeyBindingText(text.text))
                 ?? texts.FirstOrDefault(text => !_keys.Contains(text));

        foreach (TMP_Text key in _keys)
        {
            _keyParents.Add(key.transform.parent != null ? key.transform.parent.gameObject : key.gameObject);
        }

        _extraTexts.AddRange(texts.Where(text => text != _label && !_keys.Contains(text)));
        SortKeysBySiblingIndex();
    }

    private void SortKeysBySiblingIndex()
    {
        List<int> order = Enumerable.Range(0, _keys.Count)
            .OrderBy(i => _keyParents[i] != null ? _keyParents[i].transform.GetSiblingIndex() : i)
            .ToList();
        if (order.Count <= 1)
        {
            return;
        }

        List<TMP_Text> orderedKeys = [];
        List<GameObject> orderedParents = [];
        foreach (int index in order)
        {
            orderedKeys.Add(_keys[index]);
            orderedParents.Add(_keyParents[index]);
        }

        _keys.Clear();
        _keys.AddRange(orderedKeys);
        _keyParents.Clear();
        _keyParents.AddRange(orderedParents);
    }

    private static void SetText(TMP_Text? text, string value)
    {
        if (text == null)
        {
            return;
        }

        Localization.instance?.RemoveTextFromCache(text);
        text.gameObject.SetActive(true);
        text.text = value;
    }

    private static bool LooksLikeKeyBindingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = new(text
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized.Contains("mouse") ||
               normalized.Contains("ctrl") ||
               normalized.Contains("shift") ||
               normalized.Contains("alt") ||
               normalized.Contains("button") ||
               normalized.Contains("key") ||
               normalized.Contains("sprite") ||
               normalized.Length <= 2;
    }
}
