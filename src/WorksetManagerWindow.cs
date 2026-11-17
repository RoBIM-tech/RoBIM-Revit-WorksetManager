using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private readonly List<FamilyWorksetItem> _allFamilies;

    private readonly ObservableCollection<FamilyWorksetItem> _filteredFamilies = new();
    private readonly ObservableCollection<TypeWorksetItem> _currentFamilyTypes = new();

    private readonly ComboBox _categoryCombo;
    private readonly WpfTextBox _searchBox;
    private readonly CheckBox _onlyPlacedCheckBox;
    private readonly ComboBox _bulkWorksetCombo;
    private readonly Button _btnBulkApply;

    private readonly DataGrid _familyGrid;
    private readonly DataGrid _typeGrid;
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
            ?? "0.1.0-test.1";
        Title = $"타입별 작업 세트 관리 · {version} · TEST";

        Width = 1100;
        Height = 720;
        MinWidth = 920;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RoBimTheme.Apply(this);

        // 1. 작업세트 수집
        _worksets = WorksetCollector.GetWorksets(_doc);
        var wsLookup = _worksets.ToDictionary(w => w.Id, w => w.Name);

        // 2. 패밀리 및 타입 수집
        _allFamilies = WorksetCollector.CollectFamiliesAndTypes(_doc, wsLookup);

        // UI 레이아웃 빌드
        var root = new WpfGrid { Margin = RoBimTheme.ContentMargin };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Toolbar & Filters
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Master-Detail Panels
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
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 카테고리 라벨
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // 카테고리 콤보
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 검색 라벨
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); // 검색 박스
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 배치된 것만 체크
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 공백
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 일괄 지정 콤보
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 일괄 적용 버튼

        // 카테고리 필터
        var catLabel = new TextBlock
        {
            Text = "카테고리:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold
        };
        WpfGrid.SetColumn(catLabel, 0);

        _categoryCombo = new ComboBox
        {
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        var categories = _allFamilies.Select(f => f.CategoryName).Distinct().OrderBy(c => c).ToList();
        _categoryCombo.Items.Add("전체 카테고리");
        foreach (var cat in categories) _categoryCombo.Items.Add(cat);
        _categoryCombo.SelectedIndex = 0;
        _categoryCombo.SelectionChanged += (_, _) => ApplyFilter();
        WpfGrid.SetColumn(_categoryCombo, 1);

        // 검색어 필터
        var searchLabel = new TextBlock
        {
            Text = "검색:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold
        };
        WpfGrid.SetColumn(searchLabel, 2);

        _searchBox = new WpfTextBox
        {
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        WpfGrid.SetColumn(_searchBox, 3);

        // 배치된 인스턴스만 보기
        _onlyPlacedCheckBox = new CheckBox
        {
            Content = "배치된 인스턴스만 표시",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        _onlyPlacedCheckBox.Click += (_, _) => ApplyFilter();
        WpfGrid.SetColumn(_onlyPlacedCheckBox, 4);

        // 우측 일괄 지정 영역
        var bulkLabel = new TextBlock
        {
            Text = "선택 항목 일괄 지정:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeights.SemiBold
        };

        var bulkStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        bulkStack.Children.Add(bulkLabel);

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

        WpfGrid.SetColumn(bulkStack, 6);
        WpfGrid.SetColumnSpan(bulkStack, 2);

        toolbarGrid.Children.Add(catLabel);
        toolbarGrid.Children.Add(_categoryCombo);
        toolbarGrid.Children.Add(searchLabel);
        toolbarGrid.Children.Add(_searchBox);
        toolbarGrid.Children.Add(_onlyPlacedCheckBox);
        toolbarGrid.Children.Add(bulkStack);
        toolbarPanel.Child = toolbarGrid;
        WpfGrid.SetRow(toolbarPanel, 0);
        root.Children.Add(toolbarPanel);

        // ----------------- Row 1: 좌우 분할 패널 -----------------
        var bodyGrid = new WpfGrid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56, GridUnitType.Star) }); // 좌측: 패밀리 목록
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); // 스플리터
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44, GridUnitType.Star) }); // 우측: 세부 타입 목록

        // --- 좌측 패널 (패밀리 목록) ---
        var leftPanel = new DockPanel();
        _familyHeaderLabel = new TextBlock
        {
            Text = "📁 패밀리 목록",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(_familyHeaderLabel, Dock.Top);
        leftPanel.Children.Add(_familyHeaderLabel);

        _familyGrid = CreateFamilyDataGrid();
        leftPanel.Children.Add(_familyGrid);
        WpfGrid.SetColumn(leftPanel, 0);
        bodyGrid.Children.Add(leftPanel);

        // --- 스플리터 ---
        var splitter = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent
        };
        WpfGrid.SetColumn(splitter, 1);
        bodyGrid.Children.Add(splitter);

        // --- 우측 패널 (세부 타입 목록) ---
        var rightPanel = new DockPanel();
        var rightHeaderStack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        _typeHeaderLabel = new TextBlock
        {
            Text = "📄 세부 타입 목록",
            FontWeight = FontWeights.Bold,
            FontSize = 13
        };
        var typeHintLabel = new TextBlock
        {
            Text = "※ 특정 타입만 다른 작업 세트로 지정하려면 아래에서 개별 선택하세요.",
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 0)
        };
        rightHeaderStack.Children.Add(_typeHeaderLabel);
        rightHeaderStack.Children.Add(typeHintLabel);
        DockPanel.SetDock(rightHeaderStack, Dock.Top);
        rightPanel.Children.Add(rightHeaderStack);

        _typeGrid = CreateTypeDataGrid();
        rightPanel.Children.Add(_typeGrid);
        WpfGrid.SetColumn(rightPanel, 2);
        bodyGrid.Children.Add(rightPanel);

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

    private DataGrid CreateFamilyDataGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = _filteredFamilies,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        // 1. 체크박스 컬럼
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "선택",
            Binding = new WpfBinding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(45)
        });

        // 2. 카테고리
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "카테고리",
            Binding = new WpfBinding("CategoryName"),
            IsReadOnly = true,
            Width = new DataGridLength(100)
        });

        // 3. 패밀리 이름
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "패밀리 이름",
            Binding = new WpfBinding("FamilyName"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        // 4. 타입 수
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "타입수",
            Binding = new WpfBinding("TypeCount"),
            IsReadOnly = true,
            Width = new DataGridLength(50)
        });

        // 5. 배치된 인스턴스 수
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "배치수",
            Binding = new WpfBinding("TotalInstanceCount"),
            IsReadOnly = true,
            Width = new DataGridLength(55)
        });

        // 6. 현재 작업세트 요약
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "현재 작업세트",
            Binding = new WpfBinding("CurrentWorksetNameSummary"),
            IsReadOnly = true,
            Width = new DataGridLength(110)
        });

        // 7. 패밀리 일괄 변경 작업세트 콤보박스 (핵심 기능: 선택 시 하위 모든 Type 자동 반영)
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
            Width = new DataGridLength(150)
        });

        // 패밀리 선택 시 우측에 하위 타입 표시
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is FamilyWorksetItem fam)
            {
                _typeHeaderLabel.Text = $"📄 세부 타입 목록 - {fam.FamilyName} ({fam.TypeCount}개 타입, {fam.TotalInstanceCount}개 배치됨)";
                _currentFamilyTypes.Clear();
                foreach (var t in fam.Types)
                {
                    _currentFamilyTypes.Add(t);
                }
            }
            else
            {
                _typeHeaderLabel.Text = "📄 세부 타입 목록";
                _currentFamilyTypes.Clear();
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
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = _currentFamilyTypes,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        // 1. 체크박스
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "선택",
            Binding = new WpfBinding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(45)
        });

        // 2. 타입 이름
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "타입 이름",
            Binding = new WpfBinding("TypeName"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        // 3. 배치된 인스턴스 수
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "배치수",
            Binding = new WpfBinding("InstanceCount"),
            IsReadOnly = true,
            Width = new DataGridLength(55)
        });

        // 4. 현재 작업세트
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "현재 작업세트",
            Binding = new WpfBinding("CurrentWorksetName"),
            IsReadOnly = true,
            Width = new DataGridLength(110)
        });

        // 5. 타입 개별 변경 작업세트 콤보박스 (개별 오버라이드)
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
            Width = new DataGridLength(140)
        });

        // 6. 변경 상태
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "상태",
            Binding = new WpfBinding("StatusDescription"),
            IsReadOnly = true,
            Width = new DataGridLength(90)
        });

        return grid;
    }

    private void ApplyFilter()
    {
        string selectedCat = _categoryCombo?.SelectedItem as string ?? "전체 카테고리";
        string searchText = _searchBox?.Text?.Trim() ?? string.Empty;
        bool onlyPlaced = _onlyPlacedCheckBox?.IsChecked ?? true;

        var filtered = _allFamilies.Where(f =>
        {
            // 1. 카테고리 필터
            if (selectedCat != "전체 카테고리" && !string.Equals(f.CategoryName, selectedCat, StringComparison.OrdinalIgnoreCase))
                return false;

            // 2. 배치 인스턴스 필터
            if (onlyPlaced && f.TotalInstanceCount == 0)
                return false;

            // 3. 검색어 필터 (패밀리명 또는 하위 타입명)
            if (!string.IsNullOrEmpty(searchText))
            {
                bool matchFamily = f.FamilyName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchType = f.Types.Any(t => t.TypeName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!matchFamily && !matchType) return false;
            }

            return true;
        }).ToList();

        _filteredFamilies.Clear();
        foreach (var item in filtered)
        {
            _filteredFamilies.Add(item);
        }

        _familyHeaderLabel.Text = $"📁 패밀리 목록 ({_filteredFamilies.Count}개 / 전체 {_allFamilies.Count}개)";

        if (_filteredFamilies.Count > 0)
        {
            _familyGrid.SelectedIndex = 0;
        }
        else
        {
            _currentFamilyTypes.Clear();
            _typeHeaderLabel.Text = "📄 세부 타입 목록";
        }

        UpdateSummary();
    }

    private void OnBulkApplyClick(object sender, RoutedEventArgs e)
    {
        var selectedWs = _bulkWorksetCombo.SelectedItem as WorksetInfo;
        if (selectedWs == null) return;

        // 선택된 패밀리 또는 선택된 타입들에 일괄 지정
        int appliedCount = 0;
        foreach (var fam in _allFamilies)
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

        if (appliedCount == 0)
        {
            TaskDialog.Show("일괄 지정", "선택된 패밀리 또는 타입이 없습니다.\n그리드 좌측의 [선택] 체크박스를 체크한 후 다시 시도해 주세요.");
            return;
        }

        UpdateSummary();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var fam in _allFamilies)
        {
            fam.TargetWorkset = null;
            fam.IsSelected = false;
            foreach (var t in fam.Types)
            {
                t.TargetWorkset = null;
                t.IsSelected = false;
            }
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int modifiedFamilyCount = _allFamilies.Count(f => f.Types.Any(t => t.IsModified));
        int modifiedTypeCount = _allFamilies.Sum(f => f.ModifiedTypeCount);
        int modifiedInstCount = _allFamilies.Sum(f => f.ModifiedInstanceCount);

        if (modifiedInstCount == 0 && modifiedTypeCount == 0)
        {
            _summaryLabel.Text = "변경 사항 없음";
            _summaryLabel.Foreground = Brushes.Gray;
            _applyButton.Content = "적용";
            _applyButton.IsEnabled = false;
        }
        else
        {
            _summaryLabel.Text = $"⚡ 총 {modifiedFamilyCount}개 패밀리 · {modifiedTypeCount}개 타입 · {modifiedInstCount}개 인스턴스 작업 세트 변경 예정";
            _summaryLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 102, 204));
            _applyButton.Content = $"적용 ({modifiedInstCount}개 요소)";
            _applyButton.IsEnabled = true;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var modifiedTypes = _allFamilies
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
            $"총 {modifiedTypes.Count}개 타입, {totalInstCount}개 인스턴스의 작업 세트를 변경하시겠습니까?\n\n" +
            "※ 이 작업은 단일 Undo(실행 취소)로 언제든지 되돌릴 수 있습니다.",
            TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);

        if (confirm != TaskDialogResult.Ok) return;

        int updatedElementCount = 0;
        int updatedTypeCount = 0;

        using (var trans = new Transaction(_doc, "타입별 작업 세트 일괄 변경"))
        {
            trans.Start();

            foreach (var typeItem in modifiedTypes)
            {
                int targetWsId = typeItem.TargetWorkset!.Id;
                bool anyUpdated = false;

                // 1. 배치된 모든 인스턴스의 작업 세트 변경
                foreach (var instId in typeItem.InstanceIds)
                {
                    var elem = _doc.GetElement(instId);
                    if (elem == null) continue;

                    var param = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (param != null && !param.IsReadOnly && param.AsInteger() != targetWsId)
                    {
                        param.Set(targetWsId);
                        updatedElementCount++;
                        anyUpdated = true;
                    }
                }

                // 2. 타입(ElementType/FamilySymbol) 자체의 작업 세트도 수정 가능한 경우 함께 변경
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

                if (anyUpdated) updatedTypeCount++;
            }

            trans.Commit();
        }

        TaskDialog.Show(
            "작업 세트 변경 완료",
            $"총 {updatedElementCount}개 인스턴스 요소 ({updatedTypeCount}개 타입)의 작업 세트가 성공적으로 변경되었습니다.");

        DialogResult = true;
        Close();
    }
}
