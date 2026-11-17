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

    public static List<CategoryWorksetItem> CollectHierarchy(
        Document doc,
        IReadOnlyList<WorksetInfo> standardWorksets)
    {
        var wsLookup = standardWorksets.ToDictionary(w => w.Id, w => w);

        // 1. 모든 일반 인스턴스 요소 수집 및 TypeId별 그룹화
        var instancesByTypeId = new Dictionary<ElementId, List<(ElementId ElementId, int WorksetId)>>();

        var instanceCollector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType();

        foreach (var elem in instanceCollector)
        {
            if (elem is Group) continue;

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

        // 2. 일반 ElementType 수집 및 카테고리/패밀리 구성
        var typeCollector = new FilteredElementCollector(doc)
            .OfClass(typeof(ElementType))
            .Cast<ElementType>();

        var categoriesMap = new Dictionary<string, CategoryWorksetItem>();
        var familiesMap = new Dictionary<(string Category, string Family), FamilyWorksetItem>();

        foreach (var elementType in typeCollector)
        {
            if (elementType is GroupType) continue;

            var category = elementType.Category;
            if (category == null || category.CategoryType != CategoryType.Model)
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

            // 카테고리 확보
            if (!categoriesMap.TryGetValue(categoryName, out var catItem))
            {
                catItem = new CategoryWorksetItem(categoryName);
                categoriesMap[categoryName] = catItem;
            }

            // 패밀리 확보
            var famKey = (categoryName, familyName);
            if (!familiesMap.TryGetValue(famKey, out var familyItem))
            {
                familyItem = new FamilyWorksetItem(catItem, familyName);
                familiesMap[famKey] = familyItem;
                catItem.Families.Add(familyItem);
            }

            // 인스턴스 정보 확인 및 InitialWorkset 결정
            var instanceIds = new List<ElementId>();
            WorksetInfo initialWorkset = WorksetInfo.Unplaced;

            if (instancesByTypeId.TryGetValue(elementType.Id, out var instList) && instList.Count > 0)
            {
                instanceIds = instList.Select(i => i.ElementId).ToList();
                var distinctWsIds = instList.Select(i => i.WorksetId).Distinct().ToList();

                if (distinctWsIds.Count == 1)
                {
                    int wId = distinctWsIds[0];
                    if (wsLookup.TryGetValue(wId, out var found))
                        initialWorkset = found;
                    else
                        initialWorkset = new WorksetInfo(wId, $"Workset {wId}", new WorksetId(wId));
                }
                else
                {
                    initialWorkset = WorksetInfo.Mixed;
                }
            }
            else
            {
                var typeWsParam = elementType.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (typeWsParam != null && wsLookup.TryGetValue(typeWsParam.AsInteger(), out var found))
                {
                    initialWorkset = found;
                }
            }

            var typeItem = new TypeWorksetItem(
                elementType.Id,
                typeName,
                familyItem,
                instanceIds,
                initialWorkset,
                standardWorksets);

            familyItem.Types.Add(typeItem);
        }

        // 3. 모델 그룹(Group & GroupType) 수집 및 계층 추가
        CollectGroupHierarchy(doc, standardWorksets, wsLookup, categoriesMap);

        // 4. 모든 카테고리/패밀리의 초기 작업세트 및 가용 작업세트 목록 계산
        foreach (var cat in categoriesMap.Values)
        {
            cat.CalculateInitialWorkset(standardWorksets);
        }

        return categoriesMap.Values
            .OrderBy(c => c.IsGroup ? 1 : 0)
            .ThenBy(c => c.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void CollectGroupHierarchy(
        Document doc,
        IReadOnlyList<WorksetInfo> standardWorksets,
        Dictionary<int, WorksetInfo> wsLookup,
        Dictionary<string, CategoryWorksetItem> categoriesMap)
    {
        var groupInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(Group))
            .Cast<Group>()
            .ToList();

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

        if (groupTypes.Count == 0 && groupInstances.Count == 0) return;

        string groupCategoryName = "모델 그룹 (Model Groups)";
        var groupCatItem = new CategoryWorksetItem(groupCategoryName, isGroup: true);
        var groupFamilyItem = new FamilyWorksetItem(groupCatItem, "모델 그룹", isGroup: true);
        groupCatItem.Families.Add(groupFamilyItem);

        foreach (var gt in groupTypes)
        {
            var instanceIds = new List<ElementId>();
            WorksetInfo initialWorkset = WorksetInfo.Unplaced;

            if (groupsByTypeId.TryGetValue(gt.Id, out var gList) && gList.Count > 0)
            {
                instanceIds = gList.Select(i => i.ElementId).ToList();
                var distinctWsIds = gList.Select(i => i.WorksetId).Distinct().ToList();

                if (distinctWsIds.Count == 1)
                {
                    int wId = distinctWsIds[0];
                    if (wsLookup.TryGetValue(wId, out var found))
                        initialWorkset = found;
                    else
                        initialWorkset = new WorksetInfo(wId, $"Workset {wId}", new WorksetId(wId));
                }
                else
                {
                    initialWorkset = WorksetInfo.Mixed;
                }
            }
            else
            {
                var typeWsParam = gt.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (typeWsParam != null && wsLookup.TryGetValue(typeWsParam.AsInteger(), out var found))
                {
                    initialWorkset = found;
                }
            }

            var typeItem = new TypeWorksetItem(
                gt.Id,
                gt.Name,
                groupFamilyItem,
                instanceIds,
                initialWorkset,
                standardWorksets);

            groupFamilyItem.Types.Add(typeItem);
        }

        categoriesMap[groupCategoryName] = groupCatItem;
    }
}
