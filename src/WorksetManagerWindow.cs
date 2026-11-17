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
    private readonly ComboBox _bulkWorksetCombo;
    private readonly Button _btnBulkApply;

    private readonly DataGrid _treeGrid;
    private readonly GridCheckBehavior _gridCheckBehavior;
    private readonly TextBlock _summaryLabel;
    private readonly Button _applyButton;

    private string _currentSortProperty = "Name";
    private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;

    public WorksetManagerWindow(Document doc)
    {
        _doc = doc;

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.1.0-test.4";
        Title = $"타입별 작업 세트 관리 · {version} · TEST";

        Width = 1100;
        Height = 720;
        MinWidth = 900;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RoBimTheme.Apply(this);

        // 1. 작업세트 수집
        _worksets = WorksetCollector.GetWorksets(_doc);

        // 2. 카테고리 > 패밀리 > 타입 트리 수집 (도면 및 미배치 제외, 그룹 포함)
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
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
            Background = SystemColors.ControlLightLightBrush,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };

        var toolbarGrid = new WpfGrid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 검색 라벨
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); // 검색창
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 펼치기/접기 버튼들
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 여백
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 일괄 지정 콤보 및 버튼

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
            Padding = new Thickness(4, 2, 4, 2)
        };
        _searchBox.TextChanged += (s, e) => RefreshDisplayedNodes();
        WpfGrid.SetColumn(_searchBox, 1);
        toolbarGrid.Children.Add(_searchBox);

        var expandCollapsePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var btnExpandAll = new Button
        {
            Content = "모두 펼치기",
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 4, 0)
        };
        btnExpandAll.Click += (s, e) => SetAllExpanded(true);
        expandCollapsePanel.Children.Add(btnExpandAll);

        var btnCollapseAll = new Button
        {
            Content = "모두 접기",
            Padding = new Thickness(8, 3, 8, 3)
        };
        btnCollapseAll.Click += (s, e) => SetAllExpanded(false);
        expandCollapsePanel.Children.Add(btnCollapseAll);

        WpfGrid.SetColumn(expandCollapsePanel, 2);
        toolbarGrid.Children.Add(expandCollapsePanel);

        // 일괄 지정 패널
        var bulkPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var bulkLabel = new TextBlock
        {
            Text = "선택 항목 일괄 지정:",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        };
        bulkPanel.Children.Add(bulkLabel);

        _bulkWorksetCombo = new ComboBox
        {
            Width = 180,
            ItemsSource = _worksets,
            DisplayMemberPath = "Name",
            Margin = new Thickness(0, 0, 6, 0)
        };
        if (_worksets.Count > 0) _bulkWorksetCombo.SelectedIndex = 0;
        bulkPanel.Children.Add(_bulkWorksetCombo);

        _btnBulkApply = new Button
        {
            Content = "선택 일괄 적용",
            Padding = new Thickness(10, 3, 10, 3)
        };
        _btnBulkApply.Click += OnBulkApplyClicked;
        bulkPanel.Children.Add(_btnBulkApply);

        WpfGrid.SetColumn(bulkPanel, 4);
        toolbarGrid.Children.Add(bulkPanel);

        toolbarPanel.Child = toolbarGrid;
        WpfGrid.SetRow(toolbarPanel, 0);
        root.Children.Add(toolbarPanel);

        // ----------------- Row 1: 단일 계층 Tree-Grid (DataGrid) -----------------
        _treeGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.All,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(MediaColor.FromRgb(230, 230, 230)),
            ItemsSource = _displayedNodes,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1)
        };

        _treeGrid.Sorting += OnTreeGridSorting;

        // 1) 체크박스 열
        var checkCol = new DataGridCheckBoxColumn
        {
            Header = "선택",
            Width = 46,
            CanUserSort = false,
            Binding = new WpfBinding("IsSelected")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            ElementStyle = new Style(typeof(CheckBox))
            {
                Setters =
                {
                    new Setter(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center),
                    new Setter(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center)
                }
            }
        };
        _treeGrid.Columns.Add(checkCol);

        // 2) 이름 (프로젝트 브라우저 스타일 계층 트리: 인덴트 + 토글 심볼 + 아이콘 + 이름)
        var nameCol = new DataGridTemplateColumn
        {
            Header = "이름 (프로젝트 브라우저 트리)",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            SortMemberPath = "Name"
        };
        nameCol.SortDirection = ListSortDirection.Ascending;

        var namePanelFactory = new FrameworkElementFactory(typeof(StackPanel));
        namePanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        namePanelFactory.SetBinding(StackPanel.MarginProperty, new WpfBinding("IndentMargin"));

        // 토글 버튼 (▼ / ▶)
        var toggleBtnFactory = new FrameworkElementFactory(typeof(Button));
        toggleBtnFactory.SetBinding(Button.ContentProperty, new WpfBinding("ExpandSymbol"));
        toggleBtnFactory.SetValue(Button.BackgroundProperty, Brushes.Transparent);
        toggleBtnFactory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
        toggleBtnFactory.SetValue(Button.PaddingProperty, new Thickness(2, 0, 4, 0));
        toggleBtnFactory.SetValue(Button.CursorProperty, Cursors.Hand);
        toggleBtnFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnToggleExpandClicked));
        namePanelFactory.AppendChild(toggleBtnFactory);

        // 아이콘 심볼
        var iconFactory = new FrameworkElementFactory(typeof(TextBlock));
        iconFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("IconSymbol"));
        iconFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
        iconFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        namePanelFactory.AppendChild(iconFactory);

        // 이름 텍스트
        var nameTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        nameTextFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("Name"));
        nameTextFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        namePanelFactory.AppendChild(nameTextFactory);

        nameCol.CellTemplate = new DataTemplate { VisualTree = namePanelFactory };
        _treeGrid.Columns.Add(nameCol);

        // 3) 인스턴스 수 열
        var countCol = new DataGridTextColumn
        {
            Header = "요소 수",
            Width = 90,
            Binding = new WpfBinding("InstanceCount"),
            SortMemberPath = "InstanceCount",
            ElementStyle = new Style(typeof(TextBlock))
            {
                Setters =
                {
                    new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right),
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
                    new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0))
                }
            }
        };
        _treeGrid.Columns.Add(countCol);

        // 4) 작업 세트 드롭다운 열
        var worksetCol = new DataGridTemplateColumn
        {
            Header = "작업 세트",
            Width = 240,
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
        comboFactory.SetValue(ComboBox.MarginProperty, new Thickness(4, 2, 4, 2));
        worksetCol.CellTemplate = new DataTemplate { VisualTree = comboFactory };
        _treeGrid.Columns.Add(worksetCol);

        // 5) 상태 열
        var statusCol = new DataGridTextColumn
        {
            Header = "상태",
            Width = 130,
            Binding = new WpfBinding("StatusDescription"),
            CanUserSort = false,
            ElementStyle = new Style(typeof(TextBlock))
            {
                Setters =
                {
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
                    new Setter(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0))
                }
            }
        };
        _treeGrid.Columns.Add(statusCol);

        // 표준 표 엔진 연결 (GridColumnSelection + GridCheckBehavior)
        _ = new GridColumnSelection(_treeGrid, fullRows: true);

        _gridCheckBehavior = new GridCheckBehavior(
            _treeGrid,
            cell => cell.Column == checkCol ? "all" : null,
            cell => cell.Item is WorksetHierarchyNode node ? node.IsSelected : null,
            (val, cells) =>
            {
                bool target = val == true;
                foreach (var c in cells)
                {
                    if (c.Item is WorksetHierarchyNode n)
                    {
                        n.IsSelected = target;
                    }
                }
                UpdateSummary();
            },
            blockUnmappedChecks: false,
            defaultCheckColumn: () => checkCol
        );

        WpfGrid.SetRow(_treeGrid, 1);
        root.Children.Add(_treeGrid);

        // ----------------- Row 2: 하단 요약 및 실행 버튼 바 -----------------
        var footerGrid = new WpfGrid { Margin = new Thickness(0, 10, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _summaryLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
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
            Width = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold
        };
        _applyButton.Click += OnApplyClicked;
        buttonPanel.Children.Add(_applyButton);

        var closeButton = new Button
        {
            Content = "닫기",
            Width = 90,
            Height = 32
        };
        closeButton.Click += (s, e) => Close();
        buttonPanel.Children.Add(closeButton);

        WpfGrid.SetColumn(buttonPanel, 1);
        footerGrid.Children.Add(buttonPanel);

        WpfGrid.SetRow(footerGrid, 2);
        root.Children.Add(footerGrid);

        Content = root;

        // 초기 화면 표시 및 요약 갱신
        RefreshDisplayedNodes();
    }

    private void OnToggleExpandClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is WorksetHierarchyNode node)
        {
            if (node.HasChildren)
            {
                node.IsExpanded = !node.IsExpanded;
                RefreshDisplayedNodes();
            }
        }
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
            SortNodeChildren(root);
        }
    }

    private void RefreshDisplayedNodes()
    {
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
            if (isFiltered && !MatchesFilter(node)) return;

            _displayedNodes.Add(node);

            // 검색 중이면 일치하는 자식이 있는 경우 자동 펼침 처리
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
            AddVisibleNodes(root, isFiltering);
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var allTypes = GetAllTypeNodes(_allRoots).ToList();
        int totalTypes = allTypes.Count;
        int totalInstances = allTypes.Sum(t => t.InstanceCount);

        var modifiedTypes = allTypes.Where(t => t.IsModified).ToList();
        int modTypeCount = modifiedTypes.Count;
        int modInstanceCount = modifiedTypes.Sum(t => t.InstanceCount);

        _summaryLabel.Text = $"카테고리: {_allRoots.Count}개 | 전체: {totalTypes}개 타입 ({totalInstances:N0}개 요소) | 변경 예정: {modTypeCount}개 타입 ({modInstanceCount:N0}개 요소)";
    }

    private void OnBulkApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_bulkWorksetCombo.SelectedItem is not WorksetInfo targetWorkset)
        {
            TaskDialog.Show("작업 세트 관리", "일괄 적용할 작업 세트를 선택해주세요.");
            return;
        }

        // 체크된 노드들 수집
        var selectedNodes = GetAllNodes(_allRoots).Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count == 0)
        {
            TaskDialog.Show("작업 세트 관리", "체크박스로 선택된 항목이 없습니다. 표에서 원하는 행을 체크해주세요.");
            return;
        }

        foreach (var node in selectedNodes)
        {
            node.SelectedWorkset = targetWorkset;
        }

        UpdateSummary();
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

        // 단 1개의 Transaction으로 오류 관용적 일괄 처리 수행
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
