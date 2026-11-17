using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace RoBIM.Feature;

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

public sealed class TypeWorksetItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private WorksetInfo _selectedWorkset;

    public ElementId TypeId { get; }
    public string TypeName { get; }
    public FamilyWorksetItem Parent { get; }
    public List<ElementId> InstanceIds { get; }
    public int InstanceCount => InstanceIds.Count;
    public WorksetInfo InitialWorkset { get; }
    public ObservableCollection<WorksetInfo> AvailableWorksets { get; }
    public bool IsGroup => Parent.IsGroup;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
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
                Parent.NotifyChildModified();
            }
        }
    }

    public bool IsModified => SelectedWorkset != null && !SelectedWorkset.IsSpecial && !Equals(SelectedWorkset, InitialWorkset);

    public string StatusDescription
    {
        get
        {
            if (SelectedWorkset == null || Equals(SelectedWorkset, InitialWorkset)) return "동일";
            return $"-> {SelectedWorkset.Name}";
        }
    }

    public TypeWorksetItem(
        ElementId typeId,
        string typeName,
        FamilyWorksetItem parent,
        List<ElementId> instanceIds,
        WorksetInfo initialWorkset,
        IReadOnlyList<WorksetInfo> standardWorksets)
    {
        TypeId = typeId;
        TypeName = typeName;
        Parent = parent;
        InstanceIds = instanceIds;
        InitialWorkset = initialWorkset;
        _selectedWorkset = initialWorkset;

        AvailableWorksets = new ObservableCollection<WorksetInfo>();
        if (initialWorkset.IsSpecial)
        {
            AvailableWorksets.Add(initialWorkset);
        }
        foreach (var ws in standardWorksets)
        {
            AvailableWorksets.Add(ws);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class FamilyWorksetItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private WorksetInfo _selectedWorkset;

    public CategoryWorksetItem ParentCategory { get; }
    public string CategoryName => ParentCategory.CategoryName;
    public string FamilyName { get; }
    public bool IsGroup { get; }
    public ObservableCollection<TypeWorksetItem> Types { get; }
    public int TotalInstanceCount => Types.Sum(t => t.InstanceCount);
    public int TypeCount => Types.Count;
    public WorksetInfo InitialWorkset { get; private set; }
    public ObservableCollection<WorksetInfo> AvailableWorksets { get; private set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                foreach (var t in Types)
                {
                    t.IsSelected = value;
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
                // 패밀리 레벨 변경 시 하위 모든 Type의 작업세트 자동 연동
                if (value != null && !value.IsSpecial)
                {
                    foreach (var t in Types)
                    {
                        t.SelectedWorkset = value;
                    }
                }
                NotifyChildModified();
            }
        }
    }

    public bool IsModified => SelectedWorkset != null && !SelectedWorkset.IsSpecial && !Equals(SelectedWorkset, InitialWorkset);
    public int ModifiedInstanceCount => Types.Where(t => t.IsModified).Sum(t => t.InstanceCount);
    public int ModifiedTypeCount => Types.Count(t => t.IsModified);

    public FamilyWorksetItem(CategoryWorksetItem parentCategory, string familyName, bool isGroup = false)
    {
        ParentCategory = parentCategory;
        FamilyName = familyName;
        IsGroup = isGroup;
        Types = new ObservableCollection<TypeWorksetItem>();
        InitialWorkset = WorksetInfo.Unplaced;
        _selectedWorkset = WorksetInfo.Unplaced;
        AvailableWorksets = new ObservableCollection<WorksetInfo>();
    }

    public void CalculateInitialWorkset(IReadOnlyList<WorksetInfo> standardWorksets)
    {
        if (Types.Count == 0 || TotalInstanceCount == 0)
        {
            InitialWorkset = WorksetInfo.Unplaced;
        }
        else
        {
            var distinct = Types
                .Where(t => t.InstanceCount > 0)
                .Select(t => t.InitialWorkset)
                .Distinct()
                .ToList();

            if (distinct.Count == 1 && !distinct[0].IsSpecial)
                InitialWorkset = distinct[0];
            else
                InitialWorkset = WorksetInfo.Mixed;
        }

        _selectedWorkset = InitialWorkset;

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

    public void NotifyChildModified()
    {
        OnPropertyChanged(nameof(ModifiedInstanceCount));
        OnPropertyChanged(nameof(ModifiedTypeCount));
        ParentCategory.NotifyChildModified();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CategoryWorksetItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private WorksetInfo _selectedWorkset;

    public string CategoryName { get; }
    public bool IsGroup { get; }
    public ObservableCollection<FamilyWorksetItem> Families { get; }
    public int TotalInstanceCount => Families.Sum(f => f.TotalInstanceCount);
    public int FamilyCount => Families.Count;
    public int TypeCount => Families.Sum(f => f.TypeCount);
    public WorksetInfo InitialWorkset { get; private set; }
    public ObservableCollection<WorksetInfo> AvailableWorksets { get; private set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                foreach (var f in Families)
                {
                    f.IsSelected = value;
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
                // 카테고리 레벨 변경 시 하위 모든 패밀리와 타입의 작업세트 자동 연동
                if (value != null && !value.IsSpecial)
                {
                    foreach (var f in Families)
                    {
                        f.SelectedWorkset = value;
                    }
                }
                NotifyChildModified();
            }
        }
    }

    public bool IsModified => SelectedWorkset != null && !SelectedWorkset.IsSpecial && !Equals(SelectedWorkset, InitialWorkset);
    public int ModifiedInstanceCount => Families.Sum(f => f.ModifiedInstanceCount);
    public int ModifiedTypeCount => Families.Sum(f => f.ModifiedTypeCount);

    public CategoryWorksetItem(string categoryName, bool isGroup = false)
    {
        CategoryName = categoryName;
        IsGroup = isGroup;
        Families = new ObservableCollection<FamilyWorksetItem>();
        InitialWorkset = WorksetInfo.Unplaced;
        _selectedWorkset = WorksetInfo.Unplaced;
        AvailableWorksets = new ObservableCollection<WorksetInfo>();
    }

    public void CalculateInitialWorkset(IReadOnlyList<WorksetInfo> standardWorksets)
    {
        foreach (var f in Families)
        {
            f.CalculateInitialWorkset(standardWorksets);
        }

        if (Families.Count == 0 || TotalInstanceCount == 0)
        {
            InitialWorkset = WorksetInfo.Unplaced;
        }
        else
        {
            var distinct = Families
                .SelectMany(f => f.Types)
                .Where(t => t.InstanceCount > 0)
                .Select(t => t.InitialWorkset)
                .Distinct()
                .ToList();

            if (distinct.Count == 1 && !distinct[0].IsSpecial)
                InitialWorkset = distinct[0];
            else
                InitialWorkset = WorksetInfo.Mixed;
        }

        _selectedWorkset = InitialWorkset;

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

    public void NotifyChildModified()
    {
        OnPropertyChanged(nameof(ModifiedInstanceCount));
        OnPropertyChanged(nameof(ModifiedTypeCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
