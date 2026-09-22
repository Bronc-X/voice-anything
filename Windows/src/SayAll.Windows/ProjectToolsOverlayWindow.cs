using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SayAll.Windows;

public sealed class ProjectToolsOverlayWindow : Window
{
    private static readonly Brush PanelBrush = BrushFrom("#F216191E");
    private static readonly Brush CardBrush = BrushFrom("#FF22272E");
    private static readonly Brush SelectedBrush = BrushFrom("#FFFF6A3D");
    private static readonly Brush CardBorderBrush = BrushFrom("#FF3A424C");
    private static readonly Brush TextBrush = BrushFrom("#FFF7F3EE");
    private static readonly Brush MutedBrush = BrushFrom("#FFAAB2BC");
    private readonly UniformGrid _toolGrid;
    private readonly TextBlock _selectionText;

    public ProjectToolsOverlayWindow()
    {
        Width = 630;
        Height = 346;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;

        _selectionText = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            Foreground = MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _toolGrid = new UniformGrid
        {
            Rows = 3,
            Columns = 3,
            Margin = new Thickness(0, 16, 0, 14),
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = SelectedBrush,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "PROJECT TOOLS",
                    FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TextBrush,
                },
            },
        });
        Grid.SetColumn(_selectionText, 1);
        header.Children.Add(_selectionText);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        DockPanel.SetDock(_toolGrid, Dock.Top);
        layout.Children.Add(_toolGrid);
        layout.Children.Add(new TextBlock
        {
            Text = "方向键选择   ·   OK 执行   ·   返回关闭",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            Foreground = MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        Content = new Border
        {
            Background = PanelBrush,
            BorderBrush = BrushFrom("#FF303740"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(22, 18, 22, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = 0.42,
                Color = Colors.Black,
            },
            Child = layout,
        };
    }

    public void ShowState(ProjectToolMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _toolGrid.Children.Clear();
        for (var index = 0; index < state.Items.Count; index++)
        {
            var item = state.Items[index];
            var selected = index == state.SelectedIndex;
            var card = new Border
            {
                Margin = new Thickness(5),
                Padding = new Thickness(14, 10, 14, 9),
                CornerRadius = new CornerRadius(12),
                Background = selected ? SelectedBrush : CardBrush,
                BorderBrush = selected ? SelectedBrush : CardBorderBrush,
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item.Title,
                            FontFamily = new FontFamily("Microsoft YaHei UI"),
                            FontSize = 16,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = TextBrush,
                        },
                        new TextBlock
                        {
                            Text = item.Hint,
                            Margin = new Thickness(0, 4, 0, 0),
                            FontFamily = new FontFamily("Microsoft YaHei UI"),
                            FontSize = 11,
                            Foreground = selected ? TextBrush : MutedBrush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                },
            };
            _toolGrid.Children.Add(card);
        }

        _selectionText.Text = $"{state.SelectedIndex + 1:00} / {state.Items.Count:00}  {state.SelectedItem.Title}";
        PositionAtWorkAreaEdge();
        if (!IsVisible)
        {
            Show();
        }
    }

    private void PositionAtWorkAreaEdge()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 26;
        Top = workArea.Bottom - Height - 26;
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
