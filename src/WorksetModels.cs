using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
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
    public Thickness IndentMargin => new Thickness(Level * 16, 0, 0, 0);

    public WorksetHierarchyNode? Parent { get; }
    public List<WorksetHierarchyNode> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    public List<ElementId> InstanceIds { get; } = new();
    public ElementId? ElementTypeId { get; set; }
    public bool IsGroup { get; }

    public int InstanceCount => NodeType == HierarchyNodeType.Type
        ? InstanceIds.Count
        : Children.Sum(c => c.InstanceCount);

    public WorksetInfo InitialWorkset { get; set; }
    public ObservableCollection<WorksetInfo> AvailableWorksets { get; } = new();

    public string IconSymbol => NodeType switch
    {
        HierarchyNodeType.Category => "🏷️",
        HierarchyNodeType.Family => IsGroup ? "📦" : "📁",
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

                // 상위(카테고리/패밀리)에서 작업세트 변경 시 하위 모든 자식 노드 자동 연동
                if (value != null && !value.IsSpecial)
                {
                    foreach (var child in Children)
                    {
                        child.SelectedWorkset = value;
                    }
                }
                NotifyModified();
            }
        }
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
                return modCount > 0 ? $"{modCount}개 변경예정" : "유지";
            }

            if (SelectedWorkset == null || Equals(SelectedWorkset, InitialWorkset)) return "동일";
            return $"-> {SelectedWorkset.Name}";
        }
    }

    public WorksetHierarchyNode(
        HierarchyNodeType nodeType,
        string name,
        int level,
        WorksetHierarchyNode? parent,
        bool isGroup = false)
    {
        NodeType = nodeType;
        Name = name;
        Level = level;
        Parent = parent;
        IsGroup = isGroup;
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
