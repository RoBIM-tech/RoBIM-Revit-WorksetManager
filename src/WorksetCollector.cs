using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RoBIM.Feature;

public static class WorksetCollector
{
    public static List<WorksetInfo> GetWorksets(Document doc)
    {
        var result = new List<WorksetInfo>();
        var worksets = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .ToWorksets();

        foreach (var ws in worksets)
        {
            result.Add(new WorksetInfo(ws.Id.IntegerValue, ws.Name, ws.Id));
        }

        return result.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<WorksetHierarchyNode> CollectHierarchyTree(
        Document doc,
        IReadOnlyList<WorksetInfo> standardWorksets)
    {
        var wsLookup = standardWorksets.ToDictionary(w => w.Id, w => w);

        // 1. 모든 인스턴스 요소 수집 (TypeId 있는 요소 + 룸/선 등 TypeId 없는 모델 요소)
        var instancesByTypeId = new Dictionary<ElementId, List<(ElementId ElementId, int WorksetId)>>();
        var untypedGroups = new Dictionary<(string Category, string Family, string Type, bool IsCad), List<(ElementId ElementId, int WorksetId)>>();

        var instanceCollector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType();

        foreach (var elem in instanceCollector)
        {
            if (elem is Group) continue;
            if (elem.ViewSpecific) continue;

            var category = elem.Category;
            if (category == null || IsExcludedCategory(category))
                continue;

            var wsParam = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (wsParam == null) continue;

            int wsId = wsParam.AsInteger();
            // 사용자 작업세트가 아닌 내부 시스템 작업세트(뷰/패밀리 작업세트 등)에 속하고 읽기전용인 경우 제외
            if (!wsLookup.ContainsKey(wsId) && wsParam.IsReadOnly)
                continue;

            // (A) 룸 / 공간 / 면적 (SpatialElement - TypeId가 없음)
            if (elem is SpatialElement spatial)
            {
                // 미배치 룸(Location == null)은 제외
                if (spatial.Location == null) continue;

                string catName = category.Name;
                var key = (catName, catName, catName, false);
                if (!untypedGroups.TryGetValue(key, out var uList))
                {
                    uList = new List<(ElementId ElementId, int WorksetId)>();
                    untypedGroups[key] = uList;
                }
                uList.Add((elem.Id, wsId));
                continue;
            }

            // (B) 모델 선 / 룸 경계선 / 공간 분리선 등 (CurveElement - LineStyle별로 분류)
            if (elem is CurveElement curveElem)
            {
                if (wsParam.IsReadOnly && !elem.Pinned) continue;

                var lineStyle = curveElem.LineStyle as Element;
                if (lineStyle?.Category != null && (BuiltInCategory)lineStyle.Category.Id.Value == BuiltInCategory.OST_SketchLines)
                    continue;

                string catName = category.Name;
                string famName = "모델 선 (Line Styles)";
                string typeName = !string.IsNullOrWhiteSpace(lineStyle?.Name) ? lineStyle!.Name : (!string.IsNullOrWhiteSpace(elem.Name) ? elem.Name : catName);

                var key = (catName, famName, typeName, false);
                if (!untypedGroups.TryGetValue(key, out var uList))
                {
                    uList = new List<(ElementId ElementId, int WorksetId)>();
                    untypedGroups[key] = uList;
                }
                uList.Add((elem.Id, wsId));
                continue;
            }

            var typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                if (!instancesByTypeId.TryGetValue(typeId, out var list))
                {
                    list = new List<(ElementId ElementId, int WorksetId)>();
                    instancesByTypeId[typeId] = list;
                }
                list.Add((elem.Id, wsId));
            }
            else
            {
                // 기타 TypeId가 없는 모델 요소 (예: 샤프트 개구부 등)
                if (wsParam.IsReadOnly && !elem.Pinned) continue;
                if (!wsLookup.ContainsKey(wsId)) continue;

                string catName = category.Name;
                string typeName = !string.IsNullOrWhiteSpace(elem.Name) ? elem.Name : catName;
                bool isCad = IsCadCategory(category);
                var key = (catName, catName, typeName, isCad);
                if (!untypedGroups.TryGetValue(key, out var uList))
                {
                    uList = new List<(ElementId ElementId, int WorksetId)>();
                    untypedGroups[key] = uList;
                }
                uList.Add((elem.Id, wsId));
            }
        }

        // 2. 모델에 존재하는 ElementType 수집 (배치된 요소가 있는 것만)
        var typeCollector = new FilteredElementCollector(doc)
            .OfClass(typeof(ElementType))
            .Cast<ElementType>();

        var categoriesMap = new Dictionary<string, WorksetHierarchyNode>();
        var familiesMap = new Dictionary<(string Category, string Family), WorksetHierarchyNode>();

        foreach (var elementType in typeCollector)
        {
            if (elementType is GroupType) continue;

            var category = elementType.Category;
            if (category == null || IsExcludedCategory(category))
                continue;

            // 미배치(배치된 인스턴스 수가 0인 것)는 완전히 제외
            if (!instancesByTypeId.TryGetValue(elementType.Id, out var instList) || instList.Count == 0)
                continue;

            string categoryName = category.Name;
            string familyName = elementType.FamilyName;
            if (string.IsNullOrWhiteSpace(familyName))
            {
                if (elementType is FamilySymbol fs && fs.Family != null)
                {
                    familyName = fs.Family.Name;
                }
                else
                {
                    familyName = categoryName;
                }
            }
            string typeName = elementType.Name;
            bool isCad = IsCadCategory(category);

            AddTypeNodeToHierarchy(
                categoriesMap,
                familiesMap,
                categoryName,
                familyName,
                typeName,
                elementType.Id,
                instList,
                standardWorksets,
                wsLookup,
                isCad: isCad);
        }

        // 2-2. TypeId 없는 모델 요소(룸, 모델 선, 룸 경계선 등) 계층 추가
        foreach (var kvp in untypedGroups)
        {
            var (catName, famName, typeName, isCad) = kvp.Key;
            var instList = kvp.Value;
            if (instList.Count == 0) continue;

            AddTypeNodeToHierarchy(
                categoriesMap,
                familiesMap,
                catName,
                famName,
                typeName,
                ElementId.InvalidElementId,
                instList,
                standardWorksets,
                wsLookup,
                isCad: isCad);
        }

        // 3. 모델 그룹(Group & GroupType) 수집 (배치된 그룹만 포함)
        CollectGroupHierarchy(doc, standardWorksets, wsLookup, categoriesMap);

        // 4. 패밀리 및 카테고리의 InitialWorkset 및 가용 목록 계산
        foreach (var cat in categoriesMap.Values)
        {
            CalculateCategoryAndFamilyInitialWorkset(cat, standardWorksets);
        }

        return categoriesMap.Values
            .OrderBy(c => c.IsGroup ? 1 : 0)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddTypeNodeToHierarchy(
        Dictionary<string, WorksetHierarchyNode> categoriesMap,
        Dictionary<(string Category, string Family), WorksetHierarchyNode> familiesMap,
        string categoryName,
        string familyName,
        string typeName,
        ElementId elementTypeId,
        List<(ElementId ElementId, int WorksetId)> instList,
        IReadOnlyList<WorksetInfo> standardWorksets,
        Dictionary<int, WorksetInfo> wsLookup,
        bool isCad)
    {
        if (!categoriesMap.TryGetValue(categoryName, out var catNode))
        {
            catNode = new WorksetHierarchyNode(HierarchyNodeType.Category, categoryName, 0, null, isCad: isCad);
            categoriesMap[categoryName] = catNode;
        }

        var famKey = (categoryName, familyName);
        if (!familiesMap.TryGetValue(famKey, out var famNode))
        {
            famNode = new WorksetHierarchyNode(HierarchyNodeType.Family, familyName, 1, catNode, isCad: isCad);
            familiesMap[famKey] = famNode;
            catNode.Children.Add(famNode);
        }

        var typeNode = new WorksetHierarchyNode(HierarchyNodeType.Type, typeName, 2, famNode, isCad: isCad)
        {
            ElementTypeId = elementTypeId
        };
        typeNode.InstanceIds.AddRange(instList.Select(i => i.ElementId));

        var distinctWsIds = instList.Select(i => i.WorksetId).Distinct().ToList();
        if (distinctWsIds.Count == 1)
        {
            int wId = distinctWsIds[0];
            typeNode.InitialWorkset = wsLookup.TryGetValue(wId, out var found)
                ? found
                : new WorksetInfo(wId, $"Workset {wId}", new WorksetId(wId));
        }
        else
        {
            typeNode.InitialWorkset = WorksetInfo.Mixed;
        }
        typeNode.SelectedWorkset = typeNode.InitialWorkset;
        typeNode.SetupAvailableWorksets(standardWorksets);

        famNode.Children.Add(typeNode);
    }

    public static bool IsCadCategory(Category category)
    {
        if (category == null) return false;
        string name = category.Name;
        if (string.IsNullOrEmpty(name)) return false;
        return name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dgn", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".sat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedCategory(Category category)
    {
        if (category == null) return true;
        if (category.CategoryType != CategoryType.Model) return true;

        var bic = (BuiltInCategory)category.Id.Value;
        return bic switch
        {
            BuiltInCategory.OST_Sheets => true,
            BuiltInCategory.OST_Views => true,
            BuiltInCategory.OST_Viewports => true,
            BuiltInCategory.OST_TitleBlocks => true,
            BuiltInCategory.OST_ScheduleGraphics => true,
            BuiltInCategory.OST_Cameras => true,
            BuiltInCategory.OST_RasterImages => true,
            BuiltInCategory.OST_ProjectInformation => true,
            BuiltInCategory.OST_SketchLines => true,
            _ => false
        };
    }

    private static void CollectGroupHierarchy(
        Document doc,
        IReadOnlyList<WorksetInfo> standardWorksets,
        Dictionary<int, WorksetInfo> wsLookup,
        Dictionary<string, WorksetHierarchyNode> categoriesMap)
    {
        var groupInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(Group))
            .Cast<Group>()
            .ToList();

        if (groupInstances.Count == 0) return;

        var groupsByTypeId = new Dictionary<ElementId, List<(ElementId ElementId, int WorksetId)>>();
        foreach (var grp in groupInstances)
        {
            var typeId = grp.GetTypeId();
            if (typeId == ElementId.InvalidElementId) continue;

            var wsParam = grp.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            int wsId = wsParam != null ? wsParam.AsInteger() : -1;

            if (!groupsByTypeId.TryGetValue(typeId, out var list))
            {
                list = new List<(ElementId ElementId, int WorksetId)>();
                groupsByTypeId[typeId] = list;
            }
            list.Add((grp.Id, wsId));
        }

        var groupTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(GroupType))
            .Cast<GroupType>()
            .ToList();

        string groupCategoryName = "모델 그룹 (Model Groups)";
        var groupCatNode = new WorksetHierarchyNode(HierarchyNodeType.Category, groupCategoryName, 0, null, isGroup: true);
        var groupFamNode = new WorksetHierarchyNode(HierarchyNodeType.Family, "모델 그룹", 1, groupCatNode, isGroup: true);
        groupCatNode.Children.Add(groupFamNode);

        foreach (var gt in groupTypes)
        {
            if (!groupsByTypeId.TryGetValue(gt.Id, out var gList) || gList.Count == 0)
                continue; // 미배치 그룹 제외

            var typeNode = new WorksetHierarchyNode(HierarchyNodeType.Type, gt.Name, 2, groupFamNode, isGroup: true)
            {
                ElementTypeId = gt.Id
            };
            typeNode.InstanceIds.AddRange(gList.Select(i => i.ElementId));

            var distinctWsIds = gList.Select(i => i.WorksetId).Distinct().ToList();
            if (distinctWsIds.Count == 1)
            {
                int wId = distinctWsIds[0];
                typeNode.InitialWorkset = wsLookup.TryGetValue(wId, out var found)
                    ? found
                    : new WorksetInfo(wId, $"Workset {wId}", new WorksetId(wId));
            }
            else
            {
                typeNode.InitialWorkset = WorksetInfo.Mixed;
            }
            typeNode.SelectedWorkset = typeNode.InitialWorkset;
            typeNode.SetupAvailableWorksets(standardWorksets);

            groupFamNode.Children.Add(typeNode);
        }

        if (groupFamNode.Children.Count > 0)
        {
            categoriesMap[groupCategoryName] = groupCatNode;
        }
    }

    private static void CalculateCategoryAndFamilyInitialWorkset(
        WorksetHierarchyNode catNode,
        IReadOnlyList<WorksetInfo> standardWorksets)
    {
        foreach (var famNode in catNode.Children)
        {
            var famDistinct = famNode.Children.Select(c => c.InitialWorkset).Distinct().ToList();
            if (famDistinct.Count == 1 && !famDistinct[0].IsSpecial)
                famNode.InitialWorkset = famDistinct[0];
            else
                famNode.InitialWorkset = WorksetInfo.Mixed;

            famNode.SelectedWorkset = famNode.InitialWorkset;
            famNode.SetupAvailableWorksets(standardWorksets);
        }

        var catDistinct = catNode.Children.Select(f => f.InitialWorkset).Distinct().ToList();
        if (catDistinct.Count == 1 && !catDistinct[0].IsSpecial)
            catNode.InitialWorkset = catDistinct[0];
        else
            catNode.InitialWorkset = WorksetInfo.Mixed;

        catNode.SelectedWorkset = catNode.InitialWorkset;
        catNode.SetupAvailableWorksets(standardWorksets);
    }
}
