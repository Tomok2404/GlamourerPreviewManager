using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace GlamourerPreviewManager.Windows;

public class GalleryWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    
    private string searchQuery = string.Empty;
    private bool forceExpandAll = false;
    private bool forceCollapseAll = false;

    private class FolderNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public Dictionary<string, FolderNode> Subfolders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DesignInfo> Designs { get; } = new();
    }

    public GalleryWindow(Plugin plugin) : base("Glamourer Design Preview Gallery###GPM_Gallery")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        
        Size = new Vector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        
        this.plugin = plugin;
        this.configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Top Toolbar
        DrawToolbar();
        
        ImGui.Separator();
        ImGui.Spacing();

        // Build folder tree structure from filtered designs
        var tree = BuildFolderTree();

        // Scrollable content area
        using (var child = Dalamud.Interface.Utility.Raii.ImRaii.Child("GalleryContentScroll", new Vector2(-1, -1), false))
        {
            if (child.Success)
            {
                // Draw subfolders recursively
                foreach (var subfolder in tree.Subfolders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    DrawFolderNode(subfolder);
                }

                // Draw root level designs
                if (tree.Designs.Count > 0)
                {
                    if (tree.Subfolders.Count > 0)
                    {
                        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Designs (Root)");
                        ImGui.Separator();
                        ImGui.Spacing();
                    }
                    DrawDesignsGrid(tree.Designs);
                }
                
                if (tree.Subfolders.Count == 0 && tree.Designs.Count == 0)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No designs found matching the current filters.");
                }
            }
        }

        // Reset frame action flags
        forceExpandAll = false;
        forceCollapseAll = false;
    }

    private void DrawToolbar()
    {
        // Search bar
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Search:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##GallerySearch", ref searchQuery, 100);
        
        ImGui.SameLine();
        var showOnlyWithPreviews = configuration.GalleryShowOnlyWithPreviews;
        if (ImGui.Checkbox("Show only with previews", ref showOnlyWithPreviews))
        {
            configuration.GalleryShowOnlyWithPreviews = showOnlyWithPreviews;
            configuration.Save();
        }
        
        ImGui.SameLine(0f, 15f * ImGuiHelpers.GlobalScale);
        
        // Expand/Collapse all buttons
        if (ImGui.Button("Expand All"))
        {
            forceExpandAll = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Collapse All"))
        {
            forceCollapseAll = true;
        }
        
        // Slider for card width
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        var cardWidth = configuration.GalleryCardWidth;
        if (ImGui.SliderFloat("Card Size", ref cardWidth, 100f, 300f, "%.0f px"))
        {
            configuration.GalleryCardWidth = cardWidth;
            configuration.Save();
        }
    }

    private FolderNode BuildFolderTree()
    {
        var query = searchQuery.Trim();
        var allDesigns = plugin.DesignManager.Designs;
        
        var filteredDesigns = allDesigns.AsEnumerable();
        
        if (!string.IsNullOrEmpty(query))
        {
            filteredDesigns = filteredDesigns.Where(d => 
                d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.FileSystemFolder != null && d.FileSystemFolder.Contains(query, StringComparison.OrdinalIgnoreCase))
            );
        }
        
        if (configuration.GalleryShowOnlyWithPreviews)
        {
            filteredDesigns = filteredDesigns.Where(d => d.HasPreview);
        }
        
        var rootNode = new FolderNode { Name = "Root", FullPath = "" };
        foreach (var design in filteredDesigns)
        {
            var folderPath = design.FileSystemFolder ?? string.Empty;
            var parts = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            var currentNode = rootNode;
            var currentPath = "";
            
            foreach (var part in parts)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                if (!currentNode.Subfolders.TryGetValue(part, out var subfolder))
                {
                    subfolder = new FolderNode
                    {
                        Name = part,
                        FullPath = currentPath
                    };
                    currentNode.Subfolders[part] = subfolder;
                }
                currentNode = subfolder;
            }
            currentNode.Designs.Add(design);
        }
        return rootNode;
    }

    private void DrawFolderNode(FolderNode node)
    {
        if (forceExpandAll)
        {
            ImGui.SetNextItemOpen(true);
        }
        else if (forceCollapseAll)
        {
            ImGui.SetNextItemOpen(false);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.8f, 1f, 1f));
        bool isOpen = ImGui.TreeNodeEx($"##{node.FullPath}", ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        
        ImGui.PushFont(UiBuilder.IconFont);
        var icon = isOpen ? FontAwesomeIcon.FolderOpen : FontAwesomeIcon.Folder;
        ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1.0f), icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        
        int count = node.Designs.Count;
        if (count > 0)
        {
            ImGui.TextUnformatted($"{node.Name} ({count})");
        }
        else
        {
            ImGui.TextUnformatted(node.Name);
        }

        if (isOpen)
        {
            // Draw subfolders first
            foreach (var subfolder in node.Subfolders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                DrawFolderNode(subfolder);
            }

            // Draw designs in this folder next
            if (node.Designs.Count > 0)
            {
                DrawDesignsGrid(node.Designs);
            }

            ImGui.TreePop();
        }
    }

    private void DrawDesignsGrid(List<DesignInfo> designs)
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var cardWidth = configuration.GalleryCardWidth * ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        int columns = Math.Max(1, (int)(availWidth / (cardWidth + spacing)));
        
        ImGui.Indent();
        int count = 0;
        foreach (var design in designs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (count > 0 && count % columns != 0)
            {
                ImGui.SameLine();
            }
            
            DrawDesignCard(design, cardWidth);
            count++;
        }
        ImGui.Unindent();
        ImGui.Spacing();
    }

    private float GetAspect()
    {
        switch (configuration.GalleryCardAspect)
        {
            case CropAspect.Aspect16_9: return 16f / 9f;
            case CropAspect.Aspect4_3: return 4f / 3f;
            case CropAspect.Aspect1_1: return 1f;
            case CropAspect.Aspect3_4: return 3f / 4f;
            case CropAspect.Aspect9_16:
            case CropAspect.NoCrop:
            default:
                return 9f / 16f;
        }
    }

    private void DrawDesignCard(DesignInfo design, float width)
    {
        float aspect = GetAspect();
        float imageHeight = width / aspect;
        float totalHeight = imageHeight + 40f * ImGuiHelpers.GlobalScale;
        
        ImGui.BeginGroup();
        
        Vector2 pMin = ImGui.GetCursorScreenPos();
        
        // Reserve layout space
        ImGui.Dummy(new Vector2(width, totalHeight));
        bool hovered = ImGui.IsItemHovered();
        
        var drawList = ImGui.GetWindowDrawList();
        uint bgColor = ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        uint borderColor = ImGui.GetColorU32(hovered ? new Vector4(0.3f, 0.8f, 1f, 0.8f) : new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
        
        // Draw card background
        drawList.AddRectFilled(pMin, pMin + new Vector2(width, totalHeight), bgColor, 4f);
        
        if (design.HasPreview)
        {
            var path = plugin.GetBustedImagePath(design.PreviewImagePath!);
            var texture = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
            
            if (texture != null)
            {
                // Target bounding box for the image
                Vector2 boxMin = pMin + new Vector2(2, 2);
                Vector2 boxMax = pMin + new Vector2(width - 2, imageHeight - 2);
                float boxW = width - 4f;
                float boxH = imageHeight - 4f;

                Vector2 uv0 = Vector2.Zero;
                Vector2 uv1 = Vector2.One;
                Vector2 drawMin = boxMin;
                Vector2 drawMax = boxMax;

                float targetAspect = boxW / boxH;
                float imageAspect = (float)texture.Width / texture.Height;

                if (configuration.GalleryCardContainImage)
                {
                    // Contain fit - scale image to fit within bounding box, no cropping
                    if (imageAspect > targetAspect)
                    {
                        float drawH = boxW / imageAspect;
                        float offsetY = (boxH - drawH) / 2f;
                        drawMin = boxMin + new Vector2(0, offsetY);
                        drawMax = new Vector2(boxMax.X, boxMin.Y + offsetY + drawH);
                    }
                    else
                    {
                        float drawW = boxH * imageAspect;
                        float offsetX = (boxW - drawW) / 2f;
                        drawMin = boxMin + new Vector2(offsetX, 0);
                        drawMax = new Vector2(boxMin.X + offsetX + drawW, boxMax.Y);
                    }
                }
                else
                {
                    // Cover fit - scale image to fill bounding box, cropping if aspects mismatch
                    if (imageAspect > targetAspect)
                    {
                        float ratio = targetAspect / imageAspect;
                        float margin = (1f - ratio) / 2f;
                        uv0 = new Vector2(margin, 0f);
                        uv1 = new Vector2(1f - margin, 1f);
                    }
                    else
                    {
                        float ratio = imageAspect / targetAspect;
                        float margin = (1f - ratio) / 2f;
                        uv0 = new Vector2(0f, margin);
                        uv1 = new Vector2(1f, 1f - margin);
                    }
                }
                
                // Draw image
                drawList.AddImage(texture.Handle, drawMin, drawMax, uv0, uv1);
            }
            else
            {
                // Texture failed to load placeholder
                drawList.AddRectFilled(pMin + new Vector2(2, 2), pMin + new Vector2(width - 2, imageHeight - 2), ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.8f)));
                ImGui.SetCursorScreenPos(pMin + new Vector2(width / 2 - 12f, imageHeight / 2 - 12f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 0.8f), FontAwesomeIcon.ExclamationTriangle.ToIconString());
                ImGui.PopFont();
            }
        }
        else
        {
            // Dashed placeholder card for designs without previews
            drawList.AddRect(pMin + new Vector2(2, 2), pMin + new Vector2(width - 2, imageHeight - 2), ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)), 0f, ImDrawFlags.None, 1f);
            
            ImGui.SetCursorScreenPos(pMin + new Vector2(width / 2 - 12f, imageHeight / 2 - 12f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 0.8f), FontAwesomeIcon.Plus.ToIconString());
            ImGui.PopFont();
            
            var hint = "Add Preview";
            var hintSize = ImGui.CalcTextSize(hint);
            ImGui.SetCursorScreenPos(pMin + new Vector2(width / 2 - hintSize.X / 2f, imageHeight / 2 + 15f));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 0.8f), hint);
        }
        
        // Draw design name (formatted to fit in max 2 lines, handles overflow without spaces)
        var formattedName = LimitTextLines(design.Name, width - 12f, 2);
        ImGui.SetCursorScreenPos(pMin + new Vector2(6, imageHeight + 4));
        ImGui.PushTextWrapPos(pMin.X + width - 6);
        ImGui.TextUnformatted(formattedName);
        ImGui.PopTextWrapPos();
        
        // Draw border
        drawList.AddRect(pMin, pMin + new Vector2(width, totalHeight), borderColor, 4f, ImDrawFlags.None, hovered ? 1.5f : 1f);
        
        // Handlers
        if (hovered)
        {
            if (!(design.HasPreview && ImGui.IsMouseDown(ImGuiMouseButton.Middle)))
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), design.Name);
                if (!string.IsNullOrEmpty(design.Description))
                {
                    ImGui.TextUnformatted(design.Description);
                }
                ImGui.Separator();
                ImGui.TextUnformatted("Double-click: Apply design");
                if (design.HasPreview)
                {
                    ImGui.TextUnformatted("Middle-click: Hold to zoom");
                }
                ImGui.EndTooltip();
            }
            
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Plugin.CommandManager.ProcessCommand($"/glamour apply {design.Identifier} | <me>");
                Plugin.ChatGui.Print($"[GPM] Applied design '{design.Name}' to yourself.");
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (!design.HasPreview)
                {
                    PromptAddImage(design);
                }
            }
            
            if (design.HasPreview && ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                var path = plugin.GetBustedImagePath(design.PreviewImagePath!);
                var texture = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
                if (texture != null)
                {
                    var winSize = ImGuiHelpers.MainViewport.WorkSize;
                    var imgSize = new Vector2(texture.Width, texture.Height) * configuration.ZoomScale;
                    
                    if (imgSize.X > winSize.X || imgSize.Y > winSize.Y)
                    {
                        var ratio = Math.Min(winSize.X / imgSize.X, winSize.Y / imgSize.Y);
                        imgSize *= ratio;
                    }
                    
                    var min = new Vector2(winSize.X / 2 - imgSize.X / 2, winSize.Y / 2 - imgSize.Y / 2);
                    var max = new Vector2(winSize.X / 2 + imgSize.X / 2, winSize.Y / 2 + imgSize.Y / 2);
                    
                    ImGui.GetForegroundDrawList().AddImage(texture.Handle, min, max);
                }
            }
        }
        
        // Restore layout cursor position to bottom of the card inside the group
        ImGui.SetCursorScreenPos(pMin + new Vector2(0, totalHeight));
        
        ImGui.EndGroup();
    }

    private void PromptAddImage(DesignInfo design)
    {
        plugin.FileDialogManager.OpenFileDialog(
            "Select Preview Image", 
            "Image Files{.png,.jpg,.jpeg,.webp,.bmp,.gif}", 
            (success, path) =>
            {
                if (success)
                {
                    plugin.DesignManager.UpdatePreviewImage(design.Identifier, path);
                    Plugin.ChatGui.Print($"[GPM] Successfully updated preview image for '{design.Name}'!");
                }
            });
    }

    private string LimitTextLines(string text, float maxWidth, int maxLines)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;

        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = "";

        foreach (var word in words)
        {
            var tempWord = word;
            if (ImGui.CalcTextSize(tempWord).X > maxWidth)
            {
                // Word is too long to fit on a single line at all - truncate it
                tempWord = TruncateWord(tempWord, maxWidth - ImGui.CalcTextSize("...").X) + "...";
            }

            var testLine = string.IsNullOrEmpty(currentLine) ? tempWord : $"{currentLine} {tempWord}";
            if (ImGui.CalcTextSize(testLine).X <= maxWidth)
            {
                currentLine = testLine;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }
                currentLine = tempWord;
            }
        }
        
        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        if (lines.Count > maxLines)
        {
            var lastLine = lines[maxLines - 1];
            lines[maxLines - 1] = TruncateText(lastLine, maxWidth);
            return string.Join("\n", lines.Take(maxLines));
        }

        return string.Join("\n", lines);
    }

    private string TruncateText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        
        string ellipsis = "...";
        int len = text.Length;
        while (len > 0)
        {
            string sub = text.Substring(0, len) + ellipsis;
            if (ImGui.CalcTextSize(sub).X <= maxWidth)
            {
                return sub;
            }
            len--;
        }
        return ellipsis;
    }

    private string TruncateWord(string word, float maxWidth)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;
        int len = word.Length;
        while (len > 0)
        {
            string sub = word.Substring(0, len);
            if (ImGui.CalcTextSize(sub).X <= maxWidth) return sub;
            len--;
        }
        return string.Empty;
    }
}
