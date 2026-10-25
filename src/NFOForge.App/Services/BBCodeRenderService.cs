using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NFOForge.App.Services
{
    /// <summary>
    /// Prévisualisation BBCode locale : aucun HTML, script, accès réseau ou ouverture de lien.
    /// Les balises incorrectes sont présentées comme du texte.
    /// </summary>
    public sealed class BBCodeRenderService
    {
        private const int MaxPreviewCharacters = 100_000;
        private const int MaxNestingDepth = 32;

        private static readonly Regex TagPattern = new(
            @"\[(/?)(b|i|u|color|size|center|url|img|quote|list|\*)(?:=([^\]\r\n]*))?\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private abstract record Node;
        private sealed record TextNode(string Value) : Node;
        private sealed record ElementNode(string Name, string Parameter, List<Node> Children) : Node;
        private sealed record ItemSeparatorNode : Node;

        private sealed class Frame
        {
            public Frame(string name, string parameter, string opening)
            {
                Name = name;
                Parameter = parameter;
                Opening = opening;
            }

            public string Name { get; }
            public string Parameter { get; }
            public string Opening { get; }
            public List<Node> Children { get; } = new();
        }

        public FlowDocument Render(string? bbcode)
        {
            string source = bbcode ?? string.Empty;
            if (source.Length > MaxPreviewCharacters)
                throw new ArgumentException(
                    $"Prévisualisation limitée à {MaxPreviewCharacters:N0} caractères.", nameof(bbcode));

            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(16, 24, 39)),
                PagePadding = new Thickness(12)
            };

            List<Node> nodes = Parse(source);
            RenderBlocks(nodes, document.Blocks, TextAlignment.Left);
            if (document.Blocks.Count == 0)
                document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            return document;
        }

        private static List<Node> Parse(string source)
        {
            var root = new Frame("", "", "");
            var stack = new Stack<Frame>();
            stack.Push(root);
            int position = 0;

            foreach (Match match in TagPattern.Matches(source))
            {
                if (match.Index > position)
                    stack.Peek().Children.Add(new TextNode(source.Substring(position, match.Index - position)));
                position = match.Index + match.Length;

                string name = match.Groups[2].Value.ToLowerInvariant();
                bool closing = match.Groups[1].Value == "/";
                string parameter = match.Groups[3].Value;

                if (name == "*")
                {
                    if (!closing && stack.Count > 1 && stack.Peek().Name == "list")
                        stack.Peek().Children.Add(new ItemSeparatorNode());
                    else
                        stack.Peek().Children.Add(new TextNode(match.Value));
                    continue;
                }

                if (closing)
                {
                    if (stack.Count > 1 && stack.Peek().Name == name)
                    {
                        Frame complete = stack.Pop();
                        stack.Peek().Children.Add(new ElementNode(
                            complete.Name, complete.Parameter, complete.Children));
                    }
                    else
                    {
                        stack.Peek().Children.Add(new TextNode(match.Value));
                    }
                }
                else if (stack.Count > MaxNestingDepth)
                {
                    stack.Peek().Children.Add(new TextNode(match.Value));
                }
                else
                {
                    stack.Push(new Frame(name, parameter, match.Value));
                }
            }

            if (position < source.Length)
                stack.Peek().Children.Add(new TextNode(source.Substring(position)));

            // Les balises non fermées restent visibles, sans disparition de texte.
            while (stack.Count > 1)
            {
                Frame unfinished = stack.Pop();
                stack.Peek().Children.Add(new TextNode(
                    unfinished.Opening + ReconstructSource(unfinished.Children)));
            }

            return root.Children;
        }

        private static string ReconstructSource(IEnumerable<Node> nodes)
        {
            var result = new StringBuilder();
            foreach (Node node in nodes)
            {
                switch (node)
                {
                    case TextNode text:
                        result.Append(text.Value);
                        break;
                    case ItemSeparatorNode:
                        result.Append("[*]");
                        break;
                    case ElementNode element:
                        result.Append('[').Append(element.Name);
                        if (element.Parameter.Length > 0)
                            result.Append('=').Append(element.Parameter);
                        result.Append(']');
                        result.Append(ReconstructSource(element.Children));
                        result.Append("[/").Append(element.Name).Append(']');
                        break;
                }
            }
            return result.ToString();
        }

        private static bool IsBlock(Node node) => node is ElementNode element &&
            (element.Name == "center" || element.Name == "quote" || element.Name == "list");

        private static void RenderBlocks(
            IEnumerable<Node> nodes, BlockCollection blocks, TextAlignment alignment)
        {
            Paragraph? current = null;
            foreach (Node node in nodes)
            {
                if (node is ElementNode block && IsBlock(node))
                {
                    current = null;
                    switch (block.Name)
                    {
                        case "center":
                            var center = new Section { Margin = new Thickness(0) };
                            RenderBlocks(block.Children, center.Blocks, TextAlignment.Center);
                            blocks.Add(center);
                            break;
                        case "quote":
                            var quote = new Section
                            {
                                Margin = new Thickness(4, 6, 4, 6),
                                Padding = new Thickness(10, 6, 10, 6),
                                BorderThickness = new Thickness(3, 0, 0, 0),
                                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                                Background = new SolidColorBrush(Color.FromRgb(25, 37, 55))
                            };
                            RenderBlocks(block.Children, quote.Blocks, alignment);
                            blocks.Add(quote);
                            break;
                        case "list":
                            RenderList(block, blocks, alignment);
                            break;
                    }
                }
                else
                {
                    current ??= new Paragraph
                    {
                        Margin = new Thickness(0),
                        TextAlignment = alignment
                    };
                    if (current.Parent is null)
                        blocks.Add(current);
                    RenderInline(node, current.Inlines);
                }
            }
        }

        private static void RenderList(ElementNode element, BlockCollection blocks, TextAlignment alignment)
        {
            var list = new System.Windows.Documents.List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(12, 3, 0, 5)
            };

            // Le texte blanc entre [list] et le premier [*] ne doit pas
            // provoquer la création d'une puce vide.
            var groups = new List<List<Node>>();
            List<Node> current = new();

            foreach (Node node in element.Children)
            {
                if (node is ItemSeparatorNode)
                {
                    AddNonEmptyGroup(groups, current);
                    current = new List<Node>();
                }
                else
                {
                    current.Add(node);
                }
            }

            AddNonEmptyGroup(groups, current);

            foreach (List<Node> group in groups)
            {
                var item = new ListItem();
                RenderBlocks(group, item.Blocks, alignment);
                list.ListItems.Add(item);
            }

            if (list.ListItems.Count > 0)
                blocks.Add(list);
        }

        private static void AddNonEmptyGroup(List<List<Node>> groups, List<Node> group)
        {
            // Ne retirer que les blancs aux limites de chaque élément :
            // les espaces et retours à la ligne internes sont conservés.
            var normalized = new List<Node>(group);
            const string boundaryWhitespace = " \t\r\n";

            if (normalized.Count > 0 && normalized[0] is TextNode first)
            {
                string text = first.Value.TrimStart(boundaryWhitespace.ToCharArray());
                if (text.Length == 0)
                    normalized.RemoveAt(0);
                else
                    normalized[0] = new TextNode(text);
            }

            if (normalized.Count > 0 && normalized[^1] is TextNode last)
            {
                string text = last.Value.TrimEnd(boundaryWhitespace.ToCharArray());
                if (text.Length == 0)
                    normalized.RemoveAt(normalized.Count - 1);
                else
                    normalized[^1] = new TextNode(text);
            }

            // Un groupe contenant uniquement des espaces, y compris
            // entre plusieurs TextNode, ne devient jamais un ListItem.
            if (!normalized.Exists(node => node is not TextNode text ||
                !string.IsNullOrWhiteSpace(text.Value)))
                return;

            groups.Add(normalized);
        }

        private static void RenderInline(Node node, InlineCollection target)
        {
            if (node is TextNode text)
            {
                string normalized = text.Value.Replace("\r\n", "\n").Replace('\r', '\n');
                string[] lines = normalized.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                        target.Add(new LineBreak());
                    if (lines[i].Length > 0)
                        target.Add(new Run(lines[i]));
                }
                return;
            }

            if (node is ItemSeparatorNode)
            {
                target.Add(new Run("[*]"));
                return;
            }

            if (node is not ElementNode element)
                return;

            if (element.Name == "img")
            {
                // Consomme ouverture, URL et fermeture : aucune requête réseau.
                target.Add(new Run("[Image — chargement désactivé]")
                {
                    Foreground = Brushes.LightGray,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            if (IsBlock(element))
            {
                target.Add(new Run(ReconstructSource(new[] { element })));
                return;
            }

            var span = new Span();
            ApplyStyle(span, element.Name, element.Parameter);
            foreach (Node child in element.Children)
                RenderInline(child, span.Inlines);
            target.Add(span);
        }

        private static void ApplyStyle(Span span, string name, string parameter)
        {
            switch (name)
            {
                case "b":
                    span.FontWeight = FontWeights.Bold;
                    break;
                case "i":
                    span.FontStyle = FontStyles.Italic;
                    break;
                case "u":
                    span.TextDecorations = TextDecorations.Underline;
                    break;
                case "color":
                    if (Regex.IsMatch(parameter, @"^#[0-9A-Fa-f]{6}$",
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    {
                        byte r = Convert.ToByte(parameter.Substring(1, 2), 16);
                        byte g = Convert.ToByte(parameter.Substring(3, 2), 16);
                        byte b = Convert.ToByte(parameter.Substring(5, 2), 16);
                        span.Foreground = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    break;
                case "size":
                    if (double.TryParse(parameter, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out double size))
                        span.FontSize = Math.Clamp(size, 8, 48);
                    break;
                case "url":
                    span.Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255));
                    span.TextDecorations = TextDecorations.Underline;
                    // Aucune ouverture automatique, Hyperlink ou Process.Start.
                    break;
            }
        }
    }
}