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
using Key = System.Windows.Input.Key;

namespace RoBIM.Feature;

public sealed class WorksetManagerWindow : Window
{
    private readonly Document _doc;
    private readonly List<WorksetInfo> _worksets;
    private readonly List<CategoryWorksetItem> _allCategories;

    private readonly ObservableCollection<CategoryWorksetItem> _filteredCategories = new();
    private readonly ObservableCollection<FamilyWorksetItem> _currentFamilies = new();
    private readonly ObservableCollection<TypeWorksetItem> _currentTypes = new();

    private readonly WpfTextBox _searchBox;
    private readonly CheckBox _onlyPlacedCheckBox;
    private readonly ComboBox _bulkWorksetCombo;
    private readonly Button _btnBulkApply;

    private readonly DataGrid _categoryGrid;
    private readonly DataGrid _familyGrid;
    private readonly DataGrid _typeGrid;

    private readonly TextBlock _categoryHeaderLabel;
    private readonly TextBlock _familyHeaderLabel;
    private readonly TextBlock _typeHeaderLabel;
    private readonly TextBlock _summaryLabel;
    private readonly Button _applyButton;

    public WorksetManagerWindow(Document doc)
    {
        _doc = doc;

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.1.0-test.2";
        Title = $"타입별 작업 세트 관리 · {version} · TEST";

        Width = 1240;
        Height = 740;
        MinWidth = 1000;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RoBimTheme.Apply(this);

        // 1. 작업세트 수집
        _worksets = WorksetCollector.GetWorksets(_doc);
        var wsLookup = _worksets.ToDictionary(w => w.Id, w => w.Name);

        // 2. 카테고리 > 패밀리 > 타입 3단계 계층 수집 (그룹 포함)
        _allCategories = WorksetCollector.CollectHierarchy(_doc, wsLookup);

        // UI 레이아웃 빌드
        var root = new WpfGrid { Margin = RoBimTheme.ContentMargin };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Toolbar & Filters
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: 3단 Panels
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Footer & Action Buttons

        // ----------------- Row 0: 필터 및 일괄 적용 툴바 -----------------
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
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); // 검색 박스
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 배치된 것만 체크
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 여백
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 일괄 지정 콤보 및 버튼

        var searchLabel = new TextBlock
        {
            Text = "통합 검색:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold
        };
        WpfGrid.SetColumn(searchLabel, 0);

        _searchBox = new WpfTextBox
        {
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        WpfGrid.SetColumn(_searchBox, 1);

        _onlyPlacedCheckBox = new CheckBox
        {
            Content = "배치된 인스턴스만 표시 (그룹 포함)",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        _onlyPlacedCheckBox.Click += (_, _) => ApplyFilter();
        WpfGrid.SetColumn(_onlyPlacedCheckBox, 2);

        // 우측 일괄 지정 영역
        var bulkStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        bulkStack.Children.Add(new TextBlock
        {
            Text = "선택 항목 일괄 지정:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold
        });

        _bulkWorksetCombo = new ComboBox
        {
            Height = 26,
            Width = 140,
            ItemsSource = _worksets,
            DisplayMemberPath = "Name",
            Margin = new Thickness(0, 0, 6, 0)
        };
        if (_worksets.Count > 0) _bulkWorksetCombo.SelectedIndex = 0;
        bulkStack.Children.Add(_bulkWorksetCombo);

        _btnBulkApply = new Button
        {
            Content = "선택 일괄 적용",
            Height = 26,
            Padding = new Thickness(10, 2, 10, 2)
        };
        _btnBulkApply.Click += OnBulkApplyClick;
        bulkStack.Children.Add(_btnBulkApply);

        WpfGrid.SetColumn(bulkStack, 4);

        toolbarGrid.Children.Add(searchLabel);
        toolbarGrid.Children.Add(_searchBox);
        toolbarGrid.Children.Add(_onlyPlacedCheckBox);
        toolbarGrid.Children.Add(bulkStack);
        toolbarPanel.Child = toolbarGrid;
        WpfGrid.SetRow(toolbarPanel, 0);
        root.Children.Add(toolbarPanel);

        // ----------------- Row 1: 3단 분할 패널 (카테고리 - 패밀리 - 타입) -----------------
        var bodyGrid = new WpfGrid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Star) }); // Col 0: 카테고리
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); // Splitter 1
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34, GridUnitType.Star) }); // Col 2: 패밀리
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); // Splitter 2
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Star) }); // Col 4: 세부 타입

        // --- 1열: 카테고리 패널 ---
        var catPanel = new DockPanel();
        _categoryHeaderLabel = new TextBlock
        {
            Text = "🏷️ 카테고리 목록",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(_categoryHeaderLabel, Dock.Top);
        catPanel.Children.Add(_categoryHeaderLabel);

        _categoryGrid = CreateCategoryDataGrid();
        catPanel.Children.Add(_categoryGrid);
        WpfGrid.SetColumn(catPanel, 0);
        bodyGrid.Children.Add(catPanel);

        // --- 스플리터 1 ---
        var splitter1 = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent
        };
        WpfGrid.SetColumn(splitter1, 1);
        bodyGrid.Children.Add(splitter1);

        // --- 2열: 패밀리 패널 ---
        var famPanel = new DockPanel();
        _familyHeaderLabel = new TextBlock
        {
            Text = "📁 패밀리 목록",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(_familyHeaderLabel, Dock.Top);
        famPanel.Children.Add(_familyHeaderLabel);

        _familyGrid = CreateFamilyDataGrid();
        famPanel.Children.Add(_familyGrid);
        WpfGrid.SetColumn(famPanel, 2);
        bodyGrid.Children.Add(famPanel);

        // --- 스플리터 2 ---
        var splitter2 = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent
        };
        WpfGrid.SetColumn(splitter2, 3);
        bodyGrid.Children.Add(splitter2);

        // --- 3열: 세부 타입 패널 ---
        var typePanel = new DockPanel();
        var typeHeaderStack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        _typeHeaderLabel = new TextBlock
        {
            Text = "📄 세부 타입 목록",
            FontWeight = FontWeights.Bold,
            FontSize = 13
        };
        var typeHintLabel = new TextBlock
        {
            Text = "※ 특정 타입만 다른 작업세트로 변경하려면 아래에서 개별 지정하세요. (Shift/Ctrl 다중선택 및 Space 가능)",
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 0)
        };
        typeHeaderStack.Children.Add(_typeHeaderLabel);
        typeHeaderStack.Children.Add(typeHintLabel);
        DockPanel.SetDock(typeHeaderStack, Dock.Top);
        typePanel.Children.Add(typeHeaderStack);

        _typeGrid = CreateTypeDataGrid();
        typePanel.Children.Add(_typeGrid);
        WpfGrid.SetColumn(typePanel, 4);
        bodyGrid.Children.Add(typePanel);

        WpfGrid.SetRow(bodyGrid, 1);
        root.Children.Add(bodyGrid);

        // ----------------- Row 2: 푸터 요약 및 버튼들 -----------------
        var footerGrid = new WpfGrid { Margin = new Thickness(0, 12, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _summaryLabel = new TextBlock
        {
            Text = "변경 사항 없음",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkSlateGray
        };
        WpfGrid.SetColumn(_summaryLabel, 0);

        var buttonsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnReset = new Button
        {
            Content = "변경 초기화",
            Height = 28,
            Padding = new Thickness(14, 2, 14, 2),
            Margin = new Thickness(0, 0, 8, 0)
        };
        btnReset.Click += OnResetClick;

        _applyButton = new Button
        {
            Content = "적용",
            Height = 28,
            Padding = new Thickness(20, 2, 20, 2),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _applyButton.Click += OnApplyClick;

        var btnClose = new Button
        {
            Content = "닫기",
            Height = 28,
            Padding = new Thickness(16, 2, 16, 2),
            IsCancel = true
        };
        btnClose.Click += (_, _) => Close();

        buttonsStack.Children.Add(btnReset);
        buttonsStack.Children.Add(_applyButton);
        buttonsStack.Children.Add(btnClose);
        WpfGrid.SetColumn(buttonsStack, 1);

        footerGrid.Children.Add(_summaryLabel);
        footerGrid.Children.Add(buttonsStack);
        WpfGrid.SetRow(footerGrid, 2);
        root.Children.Add(footerGrid);

        Content = root;

        // 초기 필터 적용
        ApplyFilter();
    }

    private DataGrid CreateCategoryDataGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended, // Shift, Ctrl 다중 선택 지원!
            SelectionUnit = DataGridSelectionUnit.FullRow,
            ItemsSource = _filteredCategories,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        // Space 키 입력 시 선택된 모든 행의 체크박스 일괄 토글
        grid.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Space && grid.SelectedItems.Count > 0)
            {
                var selected = grid.SelectedItems.OfType<CategoryWorksetItem>().ToList();
                if (selected.Count > 0)
                {
                    bool targetState = !selected[0].IsSelected;
                    foreach (var c in selected) c.IsSelected = targetState;
                    UpdateSummary();
                    e.Handled = true;
                }
            }
        };

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "선택",
            Binding = new WpfBinding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(40)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "카테고리",
            Binding = new WpfBinding("CategoryName"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "요소수",
            Binding = new WpfBinding("TotalInstanceCount"),
            IsReadOnly = true,
            Width = new DataGridLength(50)
        });

        // 1단계: 카테고리 전체 변경 작업세트 콤보박스
        var comboTemplate = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(ComboBox));
        factory.SetValue(ComboBox.ItemsSourceProperty, _worksets);
        factory.SetValue(ComboBox.DisplayMemberPathProperty, "Name");
        factory.SetBinding(ComboBox.SelectedItemProperty, new WpfBinding("TargetWorkset")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        factory.SetValue(FrameworkElement.HeightProperty, 24.0);
        factory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => UpdateSummary()));
        comboTemplate.VisualTree = factory;

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "카테고리 전체 변경 작업세트",
            CellTemplate = comboTemplate,
            Width = new DataGridLength(140)
        });

        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is CategoryWorksetItem cat)
            {
                _familyHeaderLabel.Text = $"📁 패밀리 목록 - {cat.CategoryName} ({cat.FamilyCount}개 패밀리)";
                _currentFamilies.Clear();
                foreach (var f in cat.Families)
                {
                    _currentFamilies.Add(f);
                }
                if (_currentFamilies.Count > 0)
                {
                    _familyGrid.SelectedIndex = 0;
                }
                else
                {
                    _currentTypes.Clear();
                    _typeHeaderLabel.Text = "📄 세부 타입 목록";
                }
            }
            else
            {
                _familyHeaderLabel.Text = "📁 패밀리 목록";
                _currentFamilies.Clear();
                _currentTypes.Clear();
                _typeHeaderLabel.Text = "📄 세부 타입 목록";
            }
        };

        return grid;
    }

    private DataGrid CreateFamilyDataGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended, // Shift, Ctrl 다중 선택 지원!
            SelectionUnit = DataGridSelectionUnit.FullRow,
            ItemsSource = _currentFamilies,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        // Space 키 입력 시 선택된 모든 행의 체크박스 일괄 토글
        grid.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Space && grid.SelectedItems.Count > 0)
            {
                var selected = grid.SelectedItems.OfType<FamilyWorksetItem>().ToList();
                if (selected.Count > 0)
                {
                    bool targetState = !selected[0].IsSelected;
                    foreach (var f in selected) f.IsSelected = targetState;
                    UpdateSummary();
                    e.Handled = true;
                }
            }
        };

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "선택",
            Binding = new WpfBinding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(40)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "패밀리 이름",
            Binding = new WpfBinding("FamilyName"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "타입수",
            Binding = new WpfBinding("TypeCount"),
            IsReadOnly = true,
            Width = new DataGridLength(45)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "요소수",
            Binding = new WpfBinding("TotalInstanceCount"),
            IsReadOnly = true,
            Width = new DataGridLength(50)
        });

        // 2단계: 패밀리 전체 변경 작업세트 콤보박스
        var comboTemplate = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(ComboBox));
        factory.SetValue(ComboBox.ItemsSourceProperty, _worksets);
        factory.SetValue(ComboBox.DisplayMemberPathProperty, "Name");
        factory.SetBinding(ComboBox.SelectedItemProperty, new WpfBinding("TargetWorkset")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        factory.SetValue(FrameworkElement.HeightProperty, 24.0);
        factory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => UpdateSummary()));
        comboTemplate.VisualTree = factory;

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "패밀리 전체 변경 작업세트",
            CellTemplate = comboTemplate,
            Width = new DataGridLength(140)
        });

        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is FamilyWorksetItem fam)
            {
                _typeHeaderLabel.Text = $"📄 세부 타입 목록 - {fam.FamilyName} ({fam.TypeCount}개 타입, {fam.TotalInstanceCount}개 배치됨)";
                _currentTypes.Clear();
                foreach (var t in fam.Types)
                {
                    _currentTypes.Add(t);
                }
            }
            else
            {
                _typeHeaderLabel.Text = "📄 세부 타입 목록";
                _currentTypes.Clear();
            }
        };

        return grid;
    }

    private DataGrid CreateTypeDataGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended, // Shift, Ctrl 다중 선택 지원!
            SelectionUnit = DataGridSelectionUnit.FullRow,
            ItemsSource = _currentTypes,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        // Space 키 입력 시 선택된 모든 행의 체크박스 일괄 토글
        grid.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Space && grid.SelectedItems.Count > 0)
            {
                var selected = grid.SelectedItems.OfType<TypeWorksetItem>().ToList();
                if (selected.Count > 0)
                {
                    bool targetState = !selected[0].IsSelected;
                    foreach (var t in selected) t.IsSelected = targetState;
                    UpdateSummary();
                    e.Handled = true;
                }
            }
        };

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "선택",
            Binding = new WpfBinding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(40)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "타입 이름",
            Binding = new WpfBinding("TypeName"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "요소수",
            Binding = new WpfBinding("InstanceCount"),
            IsReadOnly = true,
            Width = new DataGridLength(50)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "현재 작업세트",
            Binding = new WpfBinding("CurrentWorksetName"),
            IsReadOnly = true,
            Width = new DataGridLength(100)
        });

        // 3단계: 타입별 개별 변경 작업세트 콤보박스
        var comboTemplate = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(ComboBox));
        factory.SetValue(ComboBox.ItemsSourceProperty, _worksets);
        factory.SetValue(ComboBox.DisplayMemberPathProperty, "Name");
        factory.SetBinding(ComboBox.SelectedItemProperty, new WpfBinding("TargetWorkset")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        factory.SetValue(FrameworkElement.HeightProperty, 24.0);
        factory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => UpdateSummary()));
        comboTemplate.VisualTree = factory;

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "타입별 변경 작업세트",
            CellTemplate = comboTemplate,
            Width = new DataGridLength(135)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "상태",
            Binding = new WpfBinding("StatusDescription"),
            IsReadOnly = true,
            Width = new DataGridLength(80)
        });

        return grid;
    }

    private void ApplyFilter()
    {
        string searchText = _searchBox?.Text?.Trim() ?? string.Empty;
        bool onlyPlaced = _onlyPlacedCheckBox?.IsChecked ?? true;

        var filtered = _allCategories.Where(c =>
        {
            if (onlyPlaced && c.TotalInstanceCount == 0)
                return false;

            if (!string.IsNullOrEmpty(searchText))
            {
                bool matchCat = c.CategoryName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchFam = c.Families.Any(f => f.FamilyName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                bool matchType = c.Families.Any(f => f.Types.Any(t => t.TypeName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0));
                if (!matchCat && !matchFam && !matchType) return false;
            }

            return true;
        }).ToList();

        _filteredCategories.Clear();
        foreach (var item in filtered)
        {
            _filteredCategories.Add(item);
        }

        _categoryHeaderLabel.Text = $"🏷️ 카테고리 목록 ({_filteredCategories.Count}개)";

        if (_filteredCategories.Count > 0)
        {
            _categoryGrid.SelectedIndex = 0;
        }
        else
        {
            _currentFamilies.Clear();
            _currentTypes.Clear();
            _familyHeaderLabel.Text = "📁 패밀리 목록";
            _typeHeaderLabel.Text = "📄 세부 타입 목록";
        }

        UpdateSummary();
    }

    private void OnBulkApplyClick(object sender, RoutedEventArgs e)
    {
        var selectedWs = _bulkWorksetCombo.SelectedItem as WorksetInfo;
        if (selectedWs == null) return;

        int appliedCount = 0;

        // 1. 체크박스로 선택된 항목들 처리
        foreach (var cat in _allCategories)
        {
            if (cat.IsSelected)
            {
                cat.TargetWorkset = selectedWs;
                appliedCount++;
            }
            else
            {
                foreach (var fam in cat.Families)
                {
                    if (fam.IsSelected)
                    {
                        fam.TargetWorkset = selectedWs;
                        appliedCount++;
                    }
                    else
                    {
                        foreach (var t in fam.Types)
                        {
                            if (t.IsSelected)
                            {
                                t.TargetWorkset = selectedWs;
                                appliedCount++;
                            }
                        }
                    }
                }
            }
        }

        // 2. 체크박스가 없더라도 현재 Shift/Ctrl로 파랗게 다중 선택된 행들(SelectedItems)이 있다면 함께 적용
        if (_categoryGrid.SelectedItems.Count > 1)
        {
            foreach (var cat in _categoryGrid.SelectedItems.OfType<CategoryWorksetItem>())
            {
                cat.TargetWorkset = selectedWs;
                appliedCount++;
            }
        }
        if (_familyGrid.SelectedItems.Count > 1)
        {
            foreach (var fam in _familyGrid.SelectedItems.OfType<FamilyWorksetItem>())
            {
                fam.TargetWorkset = selectedWs;
                appliedCount++;
            }
        }
        if (_typeGrid.SelectedItems.Count > 1)
        {
            foreach (var t in _typeGrid.SelectedItems.OfType<TypeWorksetItem>())
            {
                t.TargetWorkset = selectedWs;
                appliedCount++;
            }
        }

        if (appliedCount == 0)
        {
            TaskDialog.Show("일괄 지정", "선택된 카테고리, 패밀리 또는 타입이 없습니다.\n목록 좌측의 [선택] 체크박스를 체크하거나 Shift/Ctrl로 행을 다중 선택한 후 다시 시도해 주세요.");
            return;
        }

        UpdateSummary();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var cat in _allCategories)
        {
            cat.TargetWorkset = null;
            cat.IsSelected = false;
            foreach (var fam in cat.Families)
            {
                fam.TargetWorkset = null;
                fam.IsSelected = false;
                foreach (var t in fam.Types)
                {
                    t.TargetWorkset = null;
                    t.IsSelected = false;
                }
            }
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int modifiedCatCount = _allCategories.Count(c => c.Families.Any(f => f.Types.Any(t => t.IsModified)));
        int modifiedFamCount = _allCategories.Sum(c => c.Families.Count(f => f.Types.Any(t => t.IsModified)));
        int modifiedTypeCount = _allCategories.Sum(c => c.ModifiedTypeCount);
        int modifiedInstCount = _allCategories.Sum(c => c.ModifiedInstanceCount);

        if (modifiedInstCount == 0 && modifiedTypeCount == 0)
        {
            _summaryLabel.Text = "변경 사항 없음";
            _summaryLabel.Foreground = Brushes.Gray;
            _applyButton.Content = "적용";
            _applyButton.IsEnabled = false;
        }
        else
        {
            _summaryLabel.Text = $"⚡ 총 {modifiedCatCount}개 카테고리 · {modifiedFamCount}개 패밀리 · {modifiedTypeCount}개 타입 · {modifiedInstCount}개 요소 변경 예정";
            _summaryLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 102, 204));
            _applyButton.Content = $"적용 ({modifiedInstCount}개 요소)";
            _applyButton.IsEnabled = true;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var modifiedTypes = _allCategories
            .SelectMany(c => c.Families)
            .SelectMany(f => f.Types)
            .Where(t => t.IsModified && t.TargetWorkset != null)
            .ToList();

        if (modifiedTypes.Count == 0)
        {
            TaskDialog.Show("작업 세트 관리", "변경할 작업 세트가 지정되지 않았습니다.");
            return;
        }

        int totalInstCount = modifiedTypes.Sum(t => t.InstanceCount);

        var confirm = TaskDialog.Show(
            "작업 세트 일괄 변경 확인",
            $"총 {modifiedTypes.Count}개 타입, {totalInstCount}개 요소의 작업 세트를 변경하시겠습니까?\n\n" +
            "※ 수정 중 특정 요소가 잠겨있거나 수정 불가하더라도 가능한 요소는 안전하게 모두 수정됩니다.\n" +
            "※ 이 작업은 단일 Undo(실행 취소)로 언제든지 되돌릴 수 있습니다.",
            TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);

        if (confirm != TaskDialogResult.Ok) return;

        int updatedElementCount = 0;
        int updatedTypeCount = 0;
        int skippedCount = 0;

        using (var trans = new Transaction(_doc, "타입별 작업 세트 일괄 변경"))
        {
            trans.Start();

            foreach (var typeItem in modifiedTypes)
            {
                int targetWsId = typeItem.TargetWorkset!.Id;
                bool anyUpdated = false;

                // 1. 배치된 모든 인스턴스(그룹 인스턴스 포함) 수정 시도 (개별 오류 격리 및 관용적 처리)
                foreach (var instId in typeItem.InstanceIds)
                {
                    try
                    {
                        var elem = _doc.GetElement(instId);
                        if (elem == null)
                        {
                            skippedCount++;
                            continue;
                        }

                        // Pinned 상태 체크: 고정된 요소는 수정 불가할 수 있음
                        if (elem.Pinned)
                        {
                            skippedCount++;
                            continue;
                        }

                        var param = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (param == null || param.IsReadOnly)
                        {
                            skippedCount++;
                            continue;
                        }

                        if (param.AsInteger() != targetWsId)
                        {
                            param.Set(targetWsId);
                            updatedElementCount++;
                            anyUpdated = true;
                        }
                    }
                    catch
                    {
                        // 그룹 종속 또는 권한 제한 등으로 실패 시 건너뛰고 계속 진행
                        skippedCount++;
                    }
                }

                // 2. 타입 심볼 자체도 수정 가능한 경우 안전하게 수정 시도
                try
                {
                    var typeElem = _doc.GetElement(typeItem.TypeId);
                    if (typeElem != null)
                    {
                        var typeParam = typeElem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (typeParam != null && !typeParam.IsReadOnly && typeParam.AsInteger() != targetWsId)
                        {
                            typeParam.Set(targetWsId);
                            anyUpdated = true;
                        }
                    }
                }
                catch
                {
                    // 타입 수정 실패 시 skip
                }

                if (anyUpdated) updatedTypeCount++;
            }

            trans.Commit();
        }

        string resultMessage = $"총 {updatedElementCount}개 요소 ({updatedTypeCount}개 타입)의 작업 세트가 성공적으로 변경되었습니다.";
        if (skippedCount > 0)
        {
            resultMessage += $"\n\n※ {skippedCount}개 요소는 잠김(Pinned) 또는 편집 권한 제한(그룹 종속 등)으로 인해 건너뛰었습니다.";
        }

        TaskDialog.Show("작업 세트 변경 완료", resultMessage);

        DialogResult = true;
        Close();
    }
}
