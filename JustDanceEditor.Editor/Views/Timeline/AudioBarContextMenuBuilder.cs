using Avalonia;
using Avalonia.Controls;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace JustDanceEditor.Editor.Views.Timeline;

internal static class AudioBarContextMenuBuilder
{
    public static ContextMenu Build(
        TimelineEditorViewModel vm,
        double beat,
        Point position,
        IReadOnlyList<(Rect rect, SectionSegment section)> sectionLabelRects,
        IReadOnlyList<(Rect rect, SignatureSegment sig)> signatureLabelRects)
    {
        SectionSegment? clickedSection = null;
        SignatureSegment? clickedSignature = null;

        foreach ((Rect rect, SectionSegment section) in sectionLabelRects)
        {
            if (rect.Contains(position))
            {
                clickedSection = section;
                break;
            }
        }

        if (clickedSection == null)
        {
            foreach ((Rect rect, SignatureSegment sig) in signatureLabelRects)
            {
                if (rect.Contains(position))
                {
                    clickedSignature = sig;
                    break;
                }
            }
        }

        ContextMenu menu = new();
        if (clickedSection != null)
            AddSectionMenu(menu, vm, clickedSection);
        else if (clickedSignature != null)
            AddSignatureMenu(menu, vm, clickedSignature);
        else
            AddCreateMenu(menu, vm, beat);

        return menu;
    }

    private static void AddSectionMenu(ContextMenu menu, TimelineEditorViewModel vm, SectionSegment section)
    {
        MenuItem changeTypeMenu = new() { Header = "Change Type" };
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            SongSectionType capturedType = type;
            SectionSegment capturedSection = section;
            MenuItem item = new() { Header = type.ToString() };
            string description = GetSectionTypeDescription(type);
            if (!string.IsNullOrEmpty(description))
                ToolTip.SetTip(item, description);
            item.Click += (_, _) => vm.ChangeSectionType(capturedSection, capturedType);
            changeTypeMenu.Items.Add(item);
        }

        menu.Items.Add(changeTypeMenu);

        MenuItem removeItem = new() { Header = "Remove Section" };
        removeItem.Click += (_, _) => vm.RemoveSection(section);
        menu.Items.Add(removeItem);
    }

    private static void AddSignatureMenu(ContextMenu menu, TimelineEditorViewModel vm, SignatureSegment signature)
    {
        MenuItem changeBeatsMenu = new() { Header = "Change Beats" };
        for (int b = 1; b <= 16; b++)
        {
            int capturedBeats = b;
            SignatureSegment capturedSignature = signature;
            MenuItem item = new() { Header = $"{b}/4" };
            item.Click += (_, _) => vm.ChangeSignatureBeats(capturedSignature, capturedBeats);
            changeBeatsMenu.Items.Add(item);
        }

        menu.Items.Add(changeBeatsMenu);

        MenuItem removeItem = new() { Header = "Remove Signature" };
        removeItem.Click += (_, _) => vm.RemoveSignature(signature);
        menu.Items.Add(removeItem);
    }

    private static void AddCreateMenu(ContextMenu menu, TimelineEditorViewModel vm, double beat)
    {
        MenuItem addSectionMenu = new() { Header = $"Add Section at Beat {beat}" };
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            SongSectionType capturedType = type;
            double capturedBeat = beat;
            MenuItem item = new() { Header = type.ToString() };
            string description = GetSectionTypeDescription(type);
            if (!string.IsNullOrEmpty(description))
                ToolTip.SetTip(item, description);
            item.Click += (_, _) => vm.AddSection(capturedBeat, capturedType);
            addSectionMenu.Items.Add(item);
        }

        menu.Items.Add(addSectionMenu);

        MenuItem addSigMenu = new() { Header = $"Add Signature at Beat {beat}" };
        for (int b = 1; b <= 16; b++)
        {
            int capturedBeats = b;
            double capturedBeat = beat;
            MenuItem item = new() { Header = $"{b}/4" };
            item.Click += (_, _) => vm.AddSignature(capturedBeat, capturedBeats);
            addSigMenu.Items.Add(item);
        }

        menu.Items.Add(addSigMenu);
    }

    public static string GetSectionTypeDescription(SongSectionType type)
    {
        FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
        if (field == null)
            return string.Empty;

        DescriptionAttribute? attr = field.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? string.Empty;
    }
}