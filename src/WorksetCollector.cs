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

        // 1. 모든 인스턴스 요소 수집 및 TypeId별 매핑 (도면/뷰 제외)
        var instancesByTypeId = new Dictionary<ElementId, List<(ElementId ElementId, int WorksetId)>>();

        var instanceCollector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType();

        foreach (var elem in instanceCollector)
        {
            if (elem is Group) continue;

            // 도면/뷰 카테고리 제외
            if (elem.Category != null && IsExcludedCategory(elem.Category))
                continue;

            var wsParam = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (wsParam == null) continue;

            var typeId = elem.GetTypeId();
            if (typeId == ElementId.InvalidElementId) continue;

            int wsId = wsParam.AsInteger();
            if (!instancesByTypeId.TryGetValue(typeId, out var list))
            {
                list = new List<(ElementId ElementId, int WorksetId)>();
                instancesByTypeId[typeId] = list;
            }
            list.Add((elem.Id, wsId));
        }

        // 2. 모델에 존재하는 ElementType 수집 (배치된 요소가 있는 것만!)
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

            // 미배치(배치된 인스턴스 수가 0인 것)는 완전히 제외!
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

            // 1단계: 카테고리 노드
            if (!categoriesMap.TryGetValue(categoryName, out var catNode))
            {
                catNode = new WorksetHierarchyNode(HierarchyNodeType.Category, categoryName, 0, null);
                categoriesMap[categoryName] = catNode;
            }

            // 2단계: 패밀리 노드
            var famKey = (categoryName, familyName);
            if (!familiesMap.TryGetValue(famKey, out var famNode))
            {
                famNode = new WorksetHierarchyNode(HierarchyNodeType.Family, familyName, 1, catNode);
                familiesMap[famKey] = famNode;
                catNode.Children.Add(famNode);
            }

            // 3단계: 타입 노드
            var typeNode = new WorksetHierarchyNode(HierarchyNodeType.Type, typeName, 2, famNode)
            {
                ElementTypeId = elementType.Id
            };
            typeNode.InstanceIds.AddRange(instList.Select(i => i.ElementId));

            // 초기 작업세트 계산
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

        // 3. 모델 그룹(Group & GroupType) 수집 (배치된 그룹만 포함!)
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
