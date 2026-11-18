using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RoBIM.Wpf;
using ComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBinding = System.Windows.Data.Binding;
using MediaColor = System.Windows.Media.Color;

namespace RoBIM.Feature;

public sealed class WorksetManagerWindow : Window
{
    private readonly Document _doc;
    private readonly List<WorksetInfo> _worksets;
    private readonly List<WorksetHierarchyNode> _allRoots;

    private readonly ObservableCollection<WorksetHierarchyNode> _displayedNodes = new();
    private readonly WpfTextBox _searchBox;
    private readonly CheckBox _includeCadCheckBox;

    private readonly DataGrid _treeGrid;
    private readonly TextBlock _summaryLabel;
    private readonly Button _applyButton;

    private string _currentSortProperty = "Name";
    private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
    private bool _isBulkUpdating = false;
    private int _selectionAnchorIndex = -1;

    private string _typeAheadBuffer = string.Empty;
    private Key _lastTypeAheadKey = Key.None;
    private DateTime _lastTypeAheadTime = DateTime.MinValue;

    private static readonly char[] HangulChoseongTable =
    {
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ',
        'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'
    };

    public WorksetManagerWindow(Document doc)
    {
        _doc = doc;

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.1.0-test.9";
        Title = $"타입별 작업 세트 관리 · {version} · TEST";

        Width = 1080;
        Height = 720;
        MinWidth = 880;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RoBimTheme.Apply(this);

        // 1. 작업세트 수집
        _worksets = WorksetCollector.GetWorksets(_doc);

        // 2. 카테고리 > 패밀리 > 타입 트리 수집 (도면 및 미배치 제외, 룸/모델선/그룹 포함)
        _allRoots = WorksetCollector.CollectHierarchyTree(_doc, _worksets);

        // 초기 정렬 (이름 오름차순)
        SortHierarchy("Name", ListSortDirection.Ascending);

        // UI 구성
        var root = new WpfGrid { Margin = RoBimTheme.ContentMargin };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: TreeGrid
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Footer

        // ----------------- Row 0: 상단 툴바 -----------------
        var toolbarPanel = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Background = SystemColors.ControlLightLightBrush,
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3)
        };

        var toolbarGrid = new WpfGrid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 검색 라벨
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); // 검색창
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 펼치기/접기/CAD체크
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 여백
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 우측 팁

        var searchLabel = new TextBlock
        {
            Text = "검색:",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        };
        WpfGrid.SetColumn(searchLabel, 0);
        toolbarGrid.Children.Add(searchLabel);

        _searchBox = new WpfTextBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 26,
            Height = 26,
            Padding = new Thickness(6, 2, 6, 2)
        };
        _searchBox.TextChanged += (s, e) => RefreshDisplayedNodes();
        WpfGrid.SetColumn(_searchBox, 1);
        toolbarGrid.Children.Add(_searchBox);

        var buttonGroupPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var btnExpandAll = new Button
        {
            Content = "모두 펼치기",
            MinHeight = 26,
            Height = 26,
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0, 4, 0)
        };
        btnExpandAll.Click += (s, e) => SetAllExpanded(true);
        buttonGroupPanel.Children.Add(btnExpandAll);

        var btnCollapseAll = new Button
        {
            Content = "모두 접기",
            MinHeight = 26,
            Height = 26,
            Padding = new Thickness(10, 2, 10, 2)
        };
        btnCollapseAll.Click += (s, e) => SetAllExpanded(false);
        buttonGroupPanel.Children.Add(btnCollapseAll);

        _includeCadCheckBox = new CheckBox
        {
            Content = "CAD(.dwg) 포함",
            IsChecked = false, // 기본적으로 CAD 숨김
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        _includeCadCheckBox.Checked += (s, e) => RefreshDisplayedNodes();
        _includeCadCheckBox.Unchecked += (s, e) => RefreshDisplayedNodes();
        buttonGroupPanel.Children.Add(_includeCadCheckBox);

        WpfGrid.SetColumn(buttonGroupPanel, 2);
        toolbarGrid.Children.Add(buttonGroupPanel);

        var tipText = new TextBlock
        {
            Text = "💡 다중 선택(Shift/Ctrl) 후 드롭다운 변경 시 일괄 적용 · 초성/글자 키로 빠른 이동",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SystemColors.GrayTextBrush,
            FontSize = 11.0
        };
        WpfGrid.SetColumn(tipText, 4);
        toolbarGrid.Children.Add(tipText);

        toolbarPanel.Child = toolbarGrid;
        WpfGrid.SetRow(toolbarPanel, 0);
        root.Children.Add(toolbarPanel);

        // ----------------- Row 1: Revit VV/VG 완벽 일치 Tree-Grid -----------------
        _treeGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            CanUserSortColumns = true,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HorizontalGridLinesBrush = new SolidColorBrush(MediaColor.FromRgb(215, 215, 215)),
            VerticalGridLinesBrush = new SolidColorBrush(MediaColor.FromRgb(215, 215, 215)),
            MinRowHeight = 22, // RoBimTheme의 기본 32px MinRowHeight를 오버라이드하여 컴팩트 22px 보장
            RowHeight = 22,
            ItemsSource = _displayedNodes,
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(170, 170, 170)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Focusable = true
        };

        KeyboardNavigation.SetTabNavigation(_treeGrid, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetDirectionalNavigation(_treeGrid, KeyboardNavigationMode.Contained);

        _treeGrid.Sorting += OnTreeGridSorting;
        _treeGrid.PreviewKeyDown += OnTreeGridPreviewKeyDown;
        _treeGrid.PreviewMouseLeftButtonDown += OnTreeGridPreviewMouseLeftButtonDown;
        _treeGrid.MouseDoubleClick += OnGridMouseDoubleClick;
        _treeGrid.SelectionChanged += OnTreeGridSelectionChanged;

        // DataGridCell 스타일 오버라이드:
        // 1) RoBimTheme의 기본 셀 패딩(8,4,8,4) 및 테두리를 0으로 제거하여 위아래 행 점선이 1px의 끊김도 없이 연결되게 함
        // 2) 개별 셀(작업세트, 상태 등)로 포커스 테두리가 절대 넘어가지 않도록 Focusable=false 처리
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(DataGridCell.FocusableProperty, false));
        cellStyle.Setters.Add(new Setter(DataGridCell.IsTabStopProperty, false));
        cellStyle.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, null));

        var selectedCellTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCellTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(MediaColor.FromRgb(0, 120, 215))));
        selectedCellTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.White));
        cellStyle.Triggers.Add(selectedCellTrigger);
        _treeGrid.CellStyle = cellStyle;

        // DataGridRow 스타일 (순수 흰색 바탕, 행 단위 포커스 활성화)
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.White));
        rowStyle.Setters.Add(new Setter(DataGridRow.FocusableProperty, true));
        rowStyle.Setters.Add(new Setter(DataGridRow.FocusVisualStyleProperty, null));
        _treeGrid.RowStyle = rowStyle;

        // 1) 카테고리 / 패밀리 / 타입 이름 열 (TreeLineControl + 아이콘 + 텍스트)
        var nameCol = new DataGridTemplateColumn
        {
            Header = "카테고리 / 패밀리 / 타입",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            SortMemberPath = "Name"
        };
        nameCol.SortDirection = ListSortDirection.Ascending;

        var namePanelFactory = new FrameworkElementFactory(typeof(StackPanel));
        namePanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        namePanelFactory.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        namePanelFactory.SetValue(StackPanel.HeightProperty, 22.0);

        // Revit VV/VG 완벽 점선 및 [+/-] 박스 렌더링 컨트롤 (TreeLineWidth 바인딩으로 아이콘 겹침 원천 차단)
        var treeLineFactory = new FrameworkElementFactory(typeof(TreeLineControl));
        treeLineFactory.SetBinding(TreeLineControl.NodeProperty, new WpfBinding());
        treeLineFactory.SetBinding(FrameworkElement.WidthProperty, new WpfBinding("TreeLineWidth"));
        treeLineFactory.SetValue(FrameworkElement.HeightProperty, 22.0);
        treeLineFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        namePanelFactory.AppendChild(treeLineFactory);

        // 아이콘 심볼
        var iconFactory = new FrameworkElementFactory(typeof(TextBlock));
        iconFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("IconSymbol"));
        iconFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 4, 0));
        iconFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconFactory.SetValue(TextBlock.FontSizeProperty, 11.5);
        namePanelFactory.AppendChild(iconFactory);

        // 이름 텍스트
        var nameTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        nameTextFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("Name"));
        nameTextFactory.SetBinding(TextBlock.FontWeightProperty, new WpfBinding("NodeFontWeight"));
        nameTextFactory.SetValue(TextBlock.FontSizeProperty, 11.5);
        nameTextFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        namePanelFactory.AppendChild(nameTextFactory);

        // 서브 요약 배지
        var subBadgeFactory = new FrameworkElementFactory(typeof(TextBlock));
        subBadgeFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("SubSummary"));
        subBadgeFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(MediaColor.FromRgb(140, 140, 140)));
        subBadgeFactory.SetValue(TextBlock.FontSizeProperty, 10.5);
        subBadgeFactory.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
        subBadgeFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        namePanelFactory.AppendChild(subBadgeFactory);

        nameCol.CellTemplate = new DataTemplate { VisualTree = namePanelFactory };
        _treeGrid.Columns.Add(nameCol);

        // 2) 인스턴스 수 열
        var countCol = new DataGridTextColumn
        {
            Header = "요소 수",
            Width = 75,
            Binding = new WpfBinding("InstanceCount"),
            SortMemberPath = "InstanceCount",
            IsReadOnly = true,
            ElementStyle = new Style(typeof(TextBlock))
            {
                Setters =
                {
                    new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right),
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
                    new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0)),
                    new Setter(TextBlock.FontSizeProperty, 11.5)
                }
            }
        };
        _treeGrid.Columns.Add(countCol);

        // 3) 작업 세트 드롭다운 열 (VV/VG 컴팩트 콤보박스, 키보드 포커스 차단)
        var worksetCol = new DataGridTemplateColumn
        {
            Header = "작업 세트",
            Width = 220,
            CanUserSort = false
        };
        var comboFactory = new FrameworkElementFactory(typeof(ComboBox));
        comboFactory.SetBinding(ComboBox.ItemsSourceProperty, new WpfBinding("AvailableWorksets"));
        comboFactory.SetBinding(ComboBox.SelectedItemProperty, new WpfBinding("SelectedWorkset")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        comboFactory.SetValue(ComboBox.DisplayMemberPathProperty, "Name");
        comboFactory.SetValue(ComboBox.VerticalAlignmentProperty, VerticalAlignment.Center);
        comboFactory.SetValue(ComboBox.MinHeightProperty, 0.0); // RoBimTheme의 MinHeight=30 오버라이드
        comboFactory.SetValue(ComboBox.HeightProperty, 19.0);
        comboFactory.SetValue(ComboBox.FontSizeProperty, 11.0);
        comboFactory.SetValue(ComboBox.PaddingProperty, new Thickness(4, 0, 4, 0));
        comboFactory.SetValue(ComboBox.MarginProperty, new Thickness(1, 0, 1, 0));
        comboFactory.SetValue(ComboBox.FocusableProperty, false); // 키보드 탐색 시 콤보박스로 포커스가 빠지지 않도록 차단
        comboFactory.SetValue(ComboBox.IsTabStopProperty, false);
        comboFactory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(OnWorksetComboSelectionChanged));
        worksetCol.CellTemplate = new DataTemplate { VisualTree = comboFactory };
        _treeGrid.Columns.Add(worksetCol);

        // 4) 상태 열
        var statusCol = new DataGridTextColumn
        {
            Header = "상태",
            Width = 120,
            Binding = new WpfBinding("StatusDescription"),
            CanUserSort = false,
            IsReadOnly = true,
            ElementStyle = new Style(typeof(TextBlock))
            {
                Setters =
                {
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
                    new Setter(TextBlock.MarginProperty, new Thickness(6, 0, 6, 0)),
                    new Setter(TextBlock.FontSizeProperty, 11.0)
                }
            }
        };
        _treeGrid.Columns.Add(statusCol);

        WpfGrid.SetRow(_treeGrid, 1);
        root.Children.Add(_treeGrid);

        // ----------------- Row 2: 하단 요약 및 실행 버튼 바 -----------------
        var footerGrid = new WpfGrid { Margin = new Thickness(0, 8, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _summaryLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5
        };
        WpfGrid.SetColumn(_summaryLabel, 0);
        footerGrid.Children.Add(_summaryLabel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _applyButton = new Button
        {
            Content = "적용 및 닫기",
            Width = 110,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold
        };
        _applyButton.Click += OnApplyClicked;
        buttonPanel.Children.Add(_applyButton);

        var closeButton = new Button
        {
            Content = "닫기",
            Width = 80,
            Height = 30
        };
        closeButton.Click += (s, e) => Close();
        buttonPanel.Children.Add(closeButton);

        WpfGrid.SetColumn(buttonPanel, 1);
        footerGrid.Children.Add(buttonPanel);

        WpfGrid.SetRow(footerGrid, 2);
        root.Children.Add(footerGrid);

        Content = root;

        Loaded += (_, _) =>
        {
            RefreshDisplayedNodes();
            if (_displayedNodes.Count > 0)
            {
                SelectSingleIndex(0);
            }
        };
    }

    private void OnTreeGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _treeGrid.SelectedItem is WorksetHierarchyNode current)
        {
            int idx = _displayedNodes.IndexOf(current);
            if (idx >= 0) _selectionAnchorIndex = idx;
        }
        UpdateSummary();
    }

    private void OnTreeGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        // 1) [+/-] 트리 라인 영역 클릭 시 토글
        var tlc = GridColumnSelection.Ancestor<TreeLineControl>(source);
        if (tlc?.Node != null && tlc.Node.HasChildren)
        {
            tlc.Node.IsExpanded = !tlc.Node.IsExpanded;
            RefreshDisplayedNodes(tlc.Node);
            e.Handled = true;
            return;
        }

        // 2) 다중 선택된 상태에서 선택된 행 중 하나의 드롭다운(ComboBox)을 클릭한 경우,
        //    다중 선택이 풀리지 않고 드롭다운만 즉시 열리도록 보호
        var combo = GridColumnSelection.Ancestor<ComboBox>(source);
        if (combo != null && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (GridColumnSelection.Ancestor<ComboBoxItem>(source) != null)
            {
                return;
            }

            var row = GridColumnSelection.Ancestor<DataGridRow>(combo);
            if (row?.DataContext is WorksetHierarchyNode clickedNode &&
                _treeGrid.SelectedItems.Count > 1 &&
                _treeGrid.SelectedItems.Contains(clickedNode))
            {
                combo.IsDropDownOpen = !combo.IsDropDownOpen;
                e.Handled = true;
                return;
            }
        }
    }

    private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (GridColumnSelection.Ancestor<ComboBox>(source) != null) return;

        var row = GridColumnSelection.Ancestor<DataGridRow>(source);
        if (row?.DataContext is WorksetHierarchyNode node && node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            RefreshDisplayedNodes(node);
            e.Handled = true;
        }
    }

    private void OnTreeGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_displayedNodes.Count == 0) return;

        var currentNode = _treeGrid.SelectedItem as WorksetHierarchyNode;
        int currentIndex = currentNode != null ? _displayedNodes.IndexOf(currentNode) : 0;
        if (currentIndex < 0) currentIndex = 0;

        // 1) 좌우 방향키: 가로 셀(작업세트, 상태)로 절대 넘어가지 않도록 무조건 차단 후 트리 제어만 수행
        if (e.Key == Key.Right)
        {
            e.Handled = true;
            if (currentNode == null)
            {
                SelectSingleIndex(0);
                return;
            }

            if (currentNode.HasChildren && !currentNode.IsExpanded)
            {
                currentNode.IsExpanded = true;
                RefreshDisplayedNodes(currentNode);
            }
            else if (currentNode.HasChildren && currentNode.IsExpanded && currentNode.Children.Count > 0)
            {
                SelectAndFocusNode(currentNode.Children[0]);
            }
            return;
        }

        if (e.Key == Key.Left)
        {
            e.Handled = true;
            if (currentNode == null)
            {
                SelectSingleIndex(0);
                return;
            }

            if (currentNode.HasChildren && currentNode.IsExpanded)
            {
                currentNode.IsExpanded = false;
                RefreshDisplayedNodes(currentNode);
            }
            else if (currentNode.Parent != null)
            {
                SelectAndFocusNode(currentNode.Parent);
            }
            return;
        }

        // 2) 상하 방향키 / Home / End / PageUp / PageDown: 항상 트리 행 단위로만 이동 (Shift 다중 범위 선택 지원)
        if (e.Key is Key.Down or Key.Up or Key.Home or Key.End or Key.PageDown or Key.PageUp)
        {
            e.Handled = true;
            int targetIndex = e.Key switch
            {
                Key.Down => Math.Min(_displayedNodes.Count - 1, currentIndex + 1),
                Key.Up => Math.Max(0, currentIndex - 1),
                Key.Home => 0,
                Key.End => _displayedNodes.Count - 1,
                Key.PageDown => Math.Min(_displayedNodes.Count - 1, currentIndex + 20),
                Key.PageUp => Math.Max(0, currentIndex - 20),
                _ => currentIndex
            };

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SelectRange(_selectionAnchorIndex >= 0 ? _selectionAnchorIndex : currentIndex, targetIndex);
            }
            else
            {
                SelectSingleIndex(targetIndex);
            }
            return;
        }

        // 3) Tab 키로 옆 칸으로 이동하는 것도 방지
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            return;
        }

        // 4) 키보드 문자/초성 입력 시 해당 항목으로 즉시 점프 (Type-Ahead 트리 탐색)
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            Key actualKey = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if ((actualKey >= Key.A && actualKey <= Key.Z) ||
                (actualKey >= Key.D0 && actualKey <= Key.D9) ||
                (actualKey >= Key.NumPad0 && actualKey <= Key.NumPad9))
            {
                e.Handled = true;
                HandleTypeAheadKey(actualKey, currentIndex);
            }
        }
    }

    private void HandleTypeAheadKey(Key key, int currentIndex)
    {
        char asciiChar = KeyToAsciiChar(key);
        if (asciiChar == '\0') return;
        char? choseong = KeyToHangulChoseong(key);

        var now = DateTime.UtcNow;
        bool isRapidSequence = (now - _lastTypeAheadTime).TotalMilliseconds <= 600;
        _lastTypeAheadTime = now;

        // 같은 키를 연타한 경우나 시간이 지난 경우: 다음 매칭 항목으로 순환 이동
        if (!isRapidSequence || (key == _lastTypeAheadKey && _typeAheadBuffer.Length == 1))
        {
            _typeAheadBuffer = asciiChar.ToString();
            _lastTypeAheadKey = key;
            JumpToNextMatchingNode(currentIndex, startFromNext: true, asciiChar, choseong, _typeAheadBuffer);
        }
        else
        {
            _typeAheadBuffer += asciiChar;
            _lastTypeAheadKey = key;
            // 먼저 누적 문자열 접두어로 검색 시도, 없으면 단일 키 순환으로 폴백
            if (!JumpToNextMatchingNode(currentIndex, startFromNext: false, asciiChar, choseong, _typeAheadBuffer))
            {
                _typeAheadBuffer = asciiChar.ToString();
                JumpToNextMatchingNode(currentIndex, startFromNext: true, asciiChar, choseong, _typeAheadBuffer);
            }
        }
    }

    private bool JumpToNextMatchingNode(int currentIndex, bool startFromNext, char asciiChar, char? choseong, string buffer)
    {
        int count = _displayedNodes.Count;
        if (count == 0) return false;

        int startOffset = startFromNext ? 1 : 0;
        for (int i = 0; i < count; i++)
        {
            int idx = (currentIndex + startOffset + i) % count;
            var node = _displayedNodes[idx];
            if (MatchesTypeAhead(node.Name, asciiChar, choseong, buffer))
            {
                SelectSingleIndex(idx);
                return true;
            }
        }
        return false;
    }

    private static bool MatchesTypeAhead(string name, char asciiChar, char? choseong, string buffer)
    {
        if (string.IsNullOrEmpty(name)) return false;

        if (buffer.Length > 1)
        {
            return name.StartsWith(buffer, StringComparison.CurrentCultureIgnoreCase);
        }

        // 1글자 검색: 영문/숫자 첫 글자 일치 또는 한글 초성 일치
        if (char.ToUpperInvariant(name[0]) == char.ToUpperInvariant(asciiChar))
            return true;

        if (choseong.HasValue)
        {
            char firstChar = name[0];
            if (firstChar == choseong.Value) return true;
            char? itemChoseong = GetHangulChoseong(firstChar);
            if (itemChoseong.HasValue && (itemChoseong.Value == choseong.Value || IsTwinChoseong(itemChoseong.Value, choseong.Value)))
                return true;
        }

        return false;
    }

    private static bool IsTwinChoseong(char itemChoseong, char keyChoseong)
    {
        return (keyChoseong == 'ㄱ' && itemChoseong == 'ㄲ')
            || (keyChoseong == 'ㄷ' && itemChoseong == 'ㄸ')
            || (keyChoseong == 'ㅂ' && itemChoseong == 'ㅃ')
            || (keyChoseong == 'ㅅ' && itemChoseong == 'ㅆ')
            || (keyChoseong == 'ㅈ' && itemChoseong == 'ㅉ');
    }

    private static char? GetHangulChoseong(char ch)
    {
        if (ch >= 0xAC00 && ch <= 0xD7A3)
        {
            int index = (ch - 0xAC00) / (21 * 28);
            if (index >= 0 && index < HangulChoseongTable.Length)
                return HangulChoseongTable[index];
        }
        return null;
    }

    private static char KeyToAsciiChar(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return (char)('a' + (key - Key.A));
        if (key >= Key.D0 && key <= Key.D9)
            return (char)('0' + (key - Key.D0));
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return (char)('0' + (key - Key.NumPad0));
        return '\0';
    }

    private static char? KeyToHangulChoseong(Key key) => key switch
    {
        Key.R => 'ㄱ',
        Key.S => 'ㄴ',
        Key.E => 'ㄷ',
        Key.F => 'ㄹ',
        Key.A => 'ㅁ',
        Key.Q => 'ㅂ',
        Key.T => 'ㅅ',
        Key.D => 'ㅇ',
        Key.W => 'ㅈ',
        Key.C => 'ㅊ',
        Key.Z => 'ㅋ',
        Key.X => 'ㅌ',
        Key.V => 'ㅍ',
        Key.G => 'ㅎ',
        _ => null
    };

    private void SelectSingleIndex(int index)
    {
        if (index < 0 || index >= _displayedNodes.Count) return;
        _selectionAnchorIndex = index;
        var target = _displayedNodes[index];
        _treeGrid.SelectedItems.Clear();
        _treeGrid.SelectedItem = target;
        FocusRow(target);
    }

    private void SelectRange(int anchorIndex, int targetIndex)
    {
        if (_displayedNodes.Count == 0) return;
        anchorIndex = Math.Clamp(anchorIndex, 0, _displayedNodes.Count - 1);
        targetIndex = Math.Clamp(targetIndex, 0, _displayedNodes.Count - 1);

        int start = Math.Min(anchorIndex, targetIndex);
        int end = Math.Max(anchorIndex, targetIndex);

        _treeGrid.SelectedItems.Clear();
        for (int i = start; i <= end; i++)
        {
            _treeGrid.SelectedItems.Add(_displayedNodes[i]);
        }

        var targetNode = _displayedNodes[targetIndex];
        _treeGrid.SelectedItem = targetNode;
        FocusRow(targetNode);
    }

    private void SelectAndFocusNode(WorksetHierarchyNode node)
    {
        int idx = _displayedNodes.IndexOf(node);
        if (idx >= 0) _selectionAnchorIndex = idx;
        _treeGrid.SelectedItems.Clear();
        _treeGrid.SelectedItem = node;
        FocusRow(node);
    }

    private void FocusRow(WorksetHierarchyNode node)
    {
        _treeGrid.ScrollIntoView(node);
        _treeGrid.Focus();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_treeGrid.IsKeyboardFocusWithin)
            {
                _treeGrid.Focus();
            }
            if (_treeGrid.ItemContainerGenerator.ContainerFromItem(node) is DataGridRow row)
            {
                row.Focus();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnWorksetComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isBulkUpdating) return;
        if (sender is not ComboBox combo || combo.DataContext is not WorksetHierarchyNode triggerNode) return;
        if (combo.SelectedItem is not WorksetInfo targetWorkset || targetWorkset.IsSpecial) return;

        // 다중 선택 상태(VV/VG 방식) 일괄 적용 검사
        var selectedNodes = _treeGrid.SelectedItems.Cast<WorksetHierarchyNode>().ToList();
        if (selectedNodes.Count > 1 && selectedNodes.Contains(triggerNode))
        {
            _isBulkUpdating = true;
            try
            {
                // 선택된 항목 중 타입(Level 2) 노드들만 추출
                var typeNodes = selectedNodes.Where(n => n.NodeType == HierarchyNodeType.Type).ToList();

                // 만약 선택된 노드 중 타입이 없고 상위 카테고리/패밀리만 선택되어 있다면 그 하위 타입들을 수집
                if (typeNodes.Count == 0)
                {
                    typeNodes = selectedNodes.SelectMany(n => GetAllTypeNodes(new[] { n })).Distinct().ToList();
                }

                foreach (var tNode in typeNodes)
                {
                    tNode.SelectedWorkset = targetWorkset;
                }

                // 상위 부모 노드들의 집계 작업세트 자동 재계산 및 드롭다운 갱신
                var parentsToUpdate = typeNodes.Select(t => t.Parent).Where(p => p != null).Distinct().ToList();
                foreach (var parent in parentsToUpdate)
                {
                    parent!.RecomputeAggregatedWorkset();
                }
            }
            finally
            {
                _isBulkUpdating = false;
            }

            UpdateSummary();
        }
        else
        {
            triggerNode.Parent?.RecomputeAggregatedWorkset();
            UpdateSummary();
        }

        // 드롭다운 변경 후에도 포커스를 트리에 유지
        _treeGrid.Focus();
    }

    private void SetAllExpanded(bool expanded)
    {
        void Recurse(WorksetHierarchyNode node)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
            {
                Recurse(child);
            }
        }

        foreach (var root in _allRoots)
        {
            Recurse(root);
        }

        RefreshDisplayedNodes();
    }

    private void OnTreeGridSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var column = e.Column;
        var property = column.SortMemberPath;
        if (string.IsNullOrEmpty(property)) return;

        var newDirection = column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        column.SortDirection = newDirection;

        foreach (var col in _treeGrid.Columns)
        {
            if (col != column) col.SortDirection = null;
        }

        _currentSortProperty = property;
        _currentSortDirection = newDirection;

        SortHierarchy(_currentSortProperty, _currentSortDirection);
        RefreshDisplayedNodes();
    }

    private void SortHierarchy(string property, ListSortDirection direction)
    {
        void SortNodeChildren(WorksetHierarchyNode node)
        {
            if (!node.HasChildren) return;

            if (property == "InstanceCount")
            {
                if (direction == ListSortDirection.Ascending)
                    node.Children.Sort((a, b) => a.InstanceCount.CompareTo(b.InstanceCount));
                else
                    node.Children.Sort((a, b) => b.InstanceCount.CompareTo(a.InstanceCount));
            }
            else
            {
                if (direction == ListSortDirection.Ascending)
                    node.Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                else
                    node.Children.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.CurrentCultureIgnoreCase));
            }

            foreach (var child in node.Children)
            {
                child.NotifySiblingChanged();
                SortNodeChildren(child);
            }
        }

        if (property == "InstanceCount")
        {
            if (direction == ListSortDirection.Ascending)
                _allRoots.Sort((a, b) => a.InstanceCount.CompareTo(b.InstanceCount));
            else
                _allRoots.Sort((a, b) => b.InstanceCount.CompareTo(a.InstanceCount));
        }
        else
        {
            if (direction == ListSortDirection.Ascending)
                _allRoots.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            else
                _allRoots.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        foreach (var root in _allRoots)
        {
            root.NotifySiblingChanged();
            SortNodeChildren(root);
        }
    }

    private void RefreshDisplayedNodes(WorksetHierarchyNode? preferredTarget = null)
    {
        // 1. 유지할 타겟 노드 결정 (선택된 노드가 접혀 숨겨지면 가장 가까운 보이는 조상으로 승격)
        var currentSelected = preferredTarget ?? _treeGrid?.SelectedItem as WorksetHierarchyNode;
        WorksetHierarchyNode? targetToRestore = null;

        bool includeCad = _includeCadCheckBox?.IsChecked == true;

        if (currentSelected != null)
        {
            var probe = currentSelected;
            while (probe != null && (!IsVisibleInTree(probe) || (!includeCad && probe.IsCad)))
            {
                probe = probe.Parent;
            }
            targetToRestore = probe;
        }

        // 컬렉션 초기화 전 검색창으로 포커스가 튀지 않도록 보호
        bool hadTreeFocus = _treeGrid != null && (_treeGrid.IsKeyboardFocusWithin || preferredTarget != null);
        if (hadTreeFocus)
        {
            _treeGrid!.Focus();
        }

        // 2. 컬렉션 갱신
        _displayedNodes.Clear();
        var filter = _searchBox?.Text?.Trim() ?? string.Empty;

        bool MatchesFilter(WorksetHierarchyNode node)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (node.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            return node.Children.Any(MatchesFilter);
        }

        void AddVisibleNodes(WorksetHierarchyNode node, bool isFiltered)
        {
            if (!includeCad && node.IsCad) return;
            if (isFiltered && !MatchesFilter(node)) return;

            _displayedNodes.Add(node);

            bool expand = node.IsExpanded || (isFiltered && !string.IsNullOrEmpty(filter));
            if (expand)
            {
                foreach (var child in node.Children)
                {
                    AddVisibleNodes(child, isFiltered);
                }
            }
        }

        bool isFiltering = !string.IsNullOrEmpty(filter);
        foreach (var root in _allRoots)
        {
            if (!includeCad && root.IsCad) continue;
            AddVisibleNodes(root, isFiltering);
        }

        // 3. 선택 및 키보드 포커스 완전 복원
        if (_treeGrid != null)
        {
            if (targetToRestore != null && _displayedNodes.Contains(targetToRestore))
            {
                if (hadTreeFocus)
                {
                    SelectAndFocusNode(targetToRestore);
                }
                else
                {
                    _treeGrid.SelectedItem = targetToRestore;
                }
            }
            else if (_displayedNodes.Count > 0 && hadTreeFocus)
            {
                SelectSingleIndex(0);
            }
        }

        UpdateSummary();
    }

    private static bool IsVisibleInTree(WorksetHierarchyNode node)
    {
        var p = node.Parent;
        while (p != null)
        {
            if (!p.IsExpanded) return false;
            p = p.Parent;
        }
        return true;
    }

    private void UpdateSummary()
    {
        bool includeCad = _includeCadCheckBox?.IsChecked == true;
        var activeRoots = _allRoots.Where(r => includeCad || !r.IsCad).ToList();
        var allTypes = GetAllTypeNodes(activeRoots).ToList();
        int totalTypes = allTypes.Count;
        int totalInstances = allTypes.Sum(t => t.InstanceCount);

        var modifiedTypes = allTypes.Where(t => t.IsModified).ToList();
        int modTypeCount = modifiedTypes.Count;
        int modInstanceCount = modifiedTypes.Sum(t => t.InstanceCount);

        int selectedCount = _treeGrid?.SelectedItems.Count ?? 0;
        string selectionText = selectedCount > 0 ? $"선택: {selectedCount}개 행 | " : string.Empty;

        _summaryLabel.Text = $"{selectionText}카테고리: {activeRoots.Count}개 | 전체: {totalTypes}개 타입 ({totalInstances:N0}개 요소) | 변경 예정: {modTypeCount}개 타입 ({modInstanceCount:N0}개 요소)";
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        var modifiedTypes = GetAllTypeNodes(_allRoots).Where(t => t.IsModified).ToList();
        if (modifiedTypes.Count == 0)
        {
            TaskDialog.Show("작업 세트 관리", "변경된 작업 세트 항목이 없습니다.");
            return;
        }

        int modTypeCount = modifiedTypes.Count;
        int modInstCount = modifiedTypes.Sum(t => t.InstanceCount);

        var confirmDialog = new TaskDialog("작업 세트 일괄 변경 확인")
        {
            MainInstruction = $"{modTypeCount}개 타입, 총 {modInstCount:N0}개 요소의 작업 세트를 변경하시겠습니까?",
            MainContent = "적용 후 단 1회의 실행 취소(Ctrl+Z)로 언제든지 모든 변경 사항을 되돌릴 수 있습니다.\n\n수정이 잠겨있거나 권한이 없는 요소는 안전하게 건너뛰며 정상 처리됩니다.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes
        };

        if (confirmDialog.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        int successCount = 0;
        int skippedCount = 0;

        using (var trans = new Transaction(_doc, "타입별 작업 세트 일괄 변경"))
        {
            trans.Start();

            foreach (var typeNode in modifiedTypes)
            {
                var targetWsId = typeNode.SelectedWorkset.RevitWorksetId;

                // 1) 타입 자체의 작업세트 파라미터 변경 시도 (패밀리 심볼)
                if (typeNode.ElementTypeId != null && typeNode.ElementTypeId != ElementId.InvalidElementId)
                {
                    var elemType = _doc.GetElement(typeNode.ElementTypeId);
                    if (elemType != null)
                    {
                        var param = elemType.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (param != null && !param.IsReadOnly)
                        {
                            try { param.Set(targetWsId.IntegerValue); } catch { }
                        }
                    }
                }

                // 2) 실제 인스턴스들의 작업세트 파라미터 변경 (오류 관용적 처리)
                foreach (var instId in typeNode.InstanceIds)
                {
                    var elem = _doc.GetElement(instId);
                    if (elem == null) continue;

                    if (elem.Pinned)
                    {
                        skippedCount++;
                        continue;
                    }

                    var param = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (param != null && !param.IsReadOnly)
                    {
                        try
                        {
                            param.Set(targetWsId.IntegerValue);
                            successCount++;
                        }
                        catch
                        {
                            skippedCount++;
                        }
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
            }

            trans.Commit();
        }

        var resultDialog = new TaskDialog("작업 세트 변경 완료")
        {
            MainInstruction = $"작업 세트 변경이 완료되었습니다.",
            MainContent = $"- 정상 변경 완료: {successCount:N0}개 요소\n- 건너뜀 (잠김/읽기전용): {skippedCount:N0}개 요소\n\nRevit에서 언제든지 Ctrl+Z를 누르면 1회로 모두 취소됩니다."
        };
        resultDialog.Show();

        DialogResult = true;
        Close();
    }

    private static IEnumerable<WorksetHierarchyNode> GetAllNodes(IEnumerable<WorksetHierarchyNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in GetAllNodes(root.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<WorksetHierarchyNode> GetAllTypeNodes(IEnumerable<WorksetHierarchyNode> roots)
    {
        foreach (var node in GetAllNodes(roots))
        {
            if (node.NodeType == HierarchyNodeType.Type)
            {
                yield return node;
            }
        }
    }
}
