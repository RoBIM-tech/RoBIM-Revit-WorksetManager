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
    public int Id { get; }
    public string Name { get; }
    public WorksetId RevitWorksetId { get; }

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
    private WorksetInfo? _targetWorkset;

    public ElementId TypeId { get; }
    public string TypeName { get; }
    public FamilyWorksetItem Parent { get; }
    public List<ElementId> InstanceIds { get; }
    public int InstanceCount => InstanceIds.Count;
    public string CurrentWorksetName { get; }
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

    public WorksetInfo? TargetWorkset
    {
        get => _targetWorkset;
        set
        {
            if (!Equals(_targetWorkset, value))
            {
                _targetWorkset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(StatusDescription));
                Parent.NotifyChildModified();
            }
        }
    }

    public bool IsModified => TargetWorkset != null && (TargetWorkset.Name != CurrentWorksetName || CurrentWorksetName == "(혼합)");

    public string StatusDescription
    {
        get
        {
            if (TargetWorkset == null) return "유지";
            if (!IsModified) return "동일";
            return $"-> {TargetWorkset.Name}";
        }
    }

    public TypeWorksetItem(
        ElementId typeId,
        string typeName,
        FamilyWorksetItem parent,
        List<ElementId> instanceIds,
        string currentWorksetName)
    {
        TypeId = typeId;
        TypeName = typeName;
        Parent = parent;
        InstanceIds = instanceIds;
        CurrentWorksetName = currentWorksetName;
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
    private WorksetInfo? _targetWorkset;

    public CategoryWorksetItem ParentCategory { get; }
    public string CategoryName => ParentCategory.CategoryName;
    public string FamilyName { get; }
    public bool IsGroup { get; }
    public ObservableCollection<TypeWorksetItem> Types { get; }
    public int TotalInstanceCount => Types.Sum(t => t.InstanceCount);
    public int TypeCount => Types.Count;

    public string CurrentWorksetNameSummary
    {
        get
        {
            if (Types.Count == 0 || TotalInstanceCount == 0) return "(미배치)";
            var distinct = Types
                .Where(t => t.InstanceCount > 0)
                .Select(t => t.CurrentWorksetName)
                .Distinct()
                .ToList();

            if (distinct.Count == 1 && distinct[0] != "(혼합)")
                return distinct[0];
            return "(혼합)";
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
                foreach (var t in Types)
                {
                    t.IsSelected = value;
                }
            }
        }
    }

    public WorksetInfo? TargetWorkset
    {
        get => _targetWorkset;
        set
        {
            if (!Equals(_targetWorkset, value))
            {
                _targetWorkset = value;
                OnPropertyChanged();
                if (value != null)
                {
                    foreach (var t in Types)
                    {
                        t.TargetWorkset = value;
                    }
                }
                NotifyChildModified();
            }
        }
    }

    public int ModifiedInstanceCount => Types.Where(t => t.IsModified).Sum(t => t.InstanceCount);
    public int ModifiedTypeCount => Types.Count(t => t.IsModified);

    public FamilyWorksetItem(CategoryWorksetItem parentCategory, string familyName, bool isGroup = false)
    {
        ParentCategory = parentCategory;
        FamilyName = familyName;
        IsGroup = isGroup;
        Types = new ObservableCollection<TypeWorksetItem>();
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
    private WorksetInfo? _targetWorkset;

    public string CategoryName { get; }
    public bool IsGroup { get; }
    public ObservableCollection<FamilyWorksetItem> Families { get; }
    public int TotalInstanceCount => Families.Sum(f => f.TotalInstanceCount);
    public int FamilyCount => Families.Count;
    public int TypeCount => Families.Sum(f => f.TypeCount);

    public string CurrentWorksetNameSummary
    {
        get
        {
            if (Families.Count == 0 || TotalInstanceCount == 0) return "(미배치)";
            var distinct = Families
                .SelectMany(f => f.Types)
                .Where(t => t.InstanceCount > 0)
                .Select(t => t.CurrentWorksetName)
                .Distinct()
                .ToList();

            if (distinct.Count == 1 && distinct[0] != "(혼합)")
                return distinct[0];
            return "(혼합)";
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
                foreach (var f in Families)
                {
                    f.IsSelected = value;
                }
            }
        }
    }

    public WorksetInfo? TargetWorkset
    {
        get => _targetWorkset;
        set
        {
            if (!Equals(_targetWorkset, value))
            {
                _targetWorkset = value;
                OnPropertyChanged();
                if (value != null)
                {
                    foreach (var f in Families)
                    {
                        f.TargetWorkset = value;
                    }
                }
                NotifyChildModified();
            }
        }
    }

    public int ModifiedInstanceCount => Families.Sum(f => f.ModifiedInstanceCount);
    public int ModifiedTypeCount => Families.Sum(f => f.ModifiedTypeCount);

    public CategoryWorksetItem(string categoryName, bool isGroup = false)
    {
        CategoryName = categoryName;
        IsGroup = isGroup;
        Families = new ObservableCollection<FamilyWorksetItem>();
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
