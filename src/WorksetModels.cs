using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;

namespace RoBIM.Feature;

public enum HierarchyNodeType
{
    Category,
    Family,
    Type
}

public sealed class WorksetInfo : IEquatable<WorksetInfo>
{
    public static readonly WorksetInfo Mixed = new(-1, "(혼합)", WorksetId.InvalidWorksetId);
    public static readonly WorksetInfo Unplaced = new(-2, "(미배치)", WorksetId.InvalidWorksetId);

    public int Id { get; }
    public string Name { get; }
    public WorksetId RevitWorksetId { get; }
    public bool IsSpecial => Id < 0;

    public WorksetInfo(int id, string name, WorksetId revitWorksetId)
    {
        Id = id;
        Name = name;
        RevitWorksetId = revitWorksetId;
    }

    public override string ToString() => Name;

    public bool Equals(WorksetInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id;
    }

    public override bool Equals(object? obj) => Equals(obj as WorksetInfo);

    public override int GetHashCode() => Id.GetHashCode();
}

public sealed class WorksetHierarchyNode : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isExpanded;
    private WorksetInfo _selectedWorkset;

    public HierarchyNodeType NodeType { get; }
    public string Name { get; }
    public int Level { get; }

    public Thickness IndentMargin => NodeType switch
    {
        HierarchyNodeType.Category => new Thickness(6, 0, 0, 0),
        HierarchyNodeType.Family => new Thickness(22, 0, 0, 0),
        _ => new Thickness(40, 0, 0, 0)
    };

    public Brush RowBackground => NodeType switch
    {
        HierarchyNodeType.Category => new SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 242, 248)), // 연한 RoBIM 블루/그레이 대분류 헤더
        HierarchyNodeType.Family => new SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 248, 250)),   // 부드러운 패밀리 헤더
        _ => Brushes.White                                                                                     // 순수 흰색 타입 행
    };

    public FontWeight NodeFontWeight => NodeType switch
    {
        HierarchyNodeType.Category => FontWeights.Bold,
        HierarchyNodeType.Family => FontWeights.SemiBold,
        _ => FontWeights.Normal
    };

    public double NodeFontSize => NodeType switch
    {
        HierarchyNodeType.Category => 12.5,
        _ => 12.0
    };

    public string SubSummary => NodeType switch
    {
        HierarchyNodeType.Category => $"[{Children.Count}개 패밀리 / {InstanceCount:N0}개 요소]",
        HierarchyNodeType.Family => $"[{Children.Count}개 타입 / {InstanceCount:N0}개 요소]",
        _ => string.Empty
    };

    public WorksetHierarchyNode? Parent { get; }
    public List<WorksetHierarchyNode> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    public List<ElementId> InstanceIds { get; } = new();
    public ElementId? ElementTypeId { get; set; }
    public bool IsGroup { get; }
    public bool IsCad { get; }

    public int InstanceCount => NodeType == HierarchyNodeType.Type
        ? InstanceIds.Count
        : Children.Sum(c => c.InstanceCount);

    public WorksetInfo InitialWorkset { get; set; }
    public ObservableCollection<WorksetInfo> AvailableWorksets { get; } = new();

    public string IconSymbol => NodeType switch
    {
        HierarchyNodeType.Category => IsCad ? "📐" : "🏷️",
        HierarchyNodeType.Family => IsGroup ? "📦" : (IsCad ? "📐" : "📁"),
        HierarchyNodeType.Type => "📄",
        _ => "▪"
    };

    public string ExpandSymbol
    {
        get
        {
            if (!HasChildren) return "   ";
            return IsExpanded ? "▼ " : "▶ ";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpandSymbol));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                // 상위 체크 시 하위 모든 자식 노드 자동 동기화
                foreach (var child in Children)
                {
                    child.IsSelected = value;
                }
            }
        }
    }

    public WorksetInfo SelectedWorkset
    {
        get => _selectedWorkset;
        set
        {
            if (!Equals(_selectedWorkset, value))
            {
                _selectedWorkset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(StatusDescription));

                // 상위(카테고리/패밀리)에서 작업세트 직접 변경 시 하위 모든 자식 노드 자동 연동
                if (NodeType != HierarchyNodeType.Type && value != null && !value.IsSpecial)
                {
                    foreach (var child in Children)
                    {
                        child.SetWorksetFromParent(value);
                    }
                }

                // 상위 부모 노드의 집계 상태 재계산
                Parent?.RecomputeAggregatedWorkset();
                NotifyModified();
            }
        }
    }

    public void SetWorksetFromParent(WorksetInfo newWorkset)
    {
        _selectedWorkset = newWorkset;
        OnPropertyChanged(nameof(SelectedWorkset));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(StatusDescription));

        if (NodeType != HierarchyNodeType.Type)
        {
            foreach (var child in Children)
            {
                child.SetWorksetFromParent(newWorkset);
            }
        }
    }

    public void RecomputeAggregatedWorkset()
    {
        if (NodeType == HierarchyNodeType.Type) return;

        // 하위 모든 유효 작업세트 검사
        var childWorksets = Children
            .Select(c => c.SelectedWorkset)
            .Where(w => w != null && !w.IsSpecial)
            .Distinct()
            .ToList();

        if (childWorksets.Count == 1 && Children.All(c => c.SelectedWorkset != null && !c.SelectedWorkset.IsSpecial))
        {
            _selectedWorkset = childWorksets[0];
        }
        else
        {
            _selectedWorkset = WorksetInfo.Mixed;
        }

        OnPropertyChanged(nameof(SelectedWorkset));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(StatusDescription));

        Parent?.RecomputeAggregatedWorkset();
    }

    public bool IsModified => NodeType == HierarchyNodeType.Type
        ? (SelectedWorkset != null && !SelectedWorkset.IsSpecial && !Equals(SelectedWorkset, InitialWorkset))
        : Children.Any(c => c.IsModified);

    public string StatusDescription
    {
        get
        {
            if (NodeType != HierarchyNodeType.Type)
            {
                int modCount = GetModifiedTypeCount();
                return modCount > 0 ? $"→ {modCount}개 변경예정" : "-";
            }

            if (SelectedWorkset == null || Equals(SelectedWorkset, InitialWorkset)) return "-";
            return $"→ {SelectedWorkset.Name}";
        }
    }

    public WorksetHierarchyNode(
        HierarchyNodeType nodeType,
        string name,
        int level,
        WorksetHierarchyNode? parent,
        bool isGroup = false,
        bool isCad = false)
    {
        NodeType = nodeType;
        Name = name;
        Level = level;
        Parent = parent;
        IsGroup = isGroup;
        IsCad = isCad || (parent?.IsCad ?? false);
        _isExpanded = true; // 기본적으로 펼침 상태
        InitialWorkset = WorksetInfo.Unplaced;
        _selectedWorkset = WorksetInfo.Unplaced;
    }

    public void SetupAvailableWorksets(IReadOnlyList<WorksetInfo> standardWorksets)
    {
        AvailableWorksets.Clear();
        if (InitialWorkset.IsSpecial)
        {
            AvailableWorksets.Add(InitialWorkset);
        }
        foreach (var ws in standardWorksets)
        {
            AvailableWorksets.Add(ws);
        }
    }

    public int GetModifiedTypeCount()
    {
        if (NodeType == HierarchyNodeType.Type) return IsModified ? 1 : 0;
        return Children.Sum(c => c.GetModifiedTypeCount());
    }

    public int GetModifiedInstanceCount()
    {
        if (NodeType == HierarchyNodeType.Type) return IsModified ? InstanceCount : 0;
        return Children.Sum(c => c.GetModifiedInstanceCount());
    }

    public void NotifyModified()
    {
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(StatusDescription));
        Parent?.NotifyModified();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
