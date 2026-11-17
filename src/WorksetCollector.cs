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

    public static List<FamilyWorksetItem> CollectFamiliesAndTypes(
        Document doc,
        IReadOnlyDictionary<int, string> worksetLookup)
    {
        // 1. 모든 인스턴스 요소 수집 및 TypeId별 그룹화
        var instancesByTypeId = new Dictionary<ElementId, List<(ElementId ElementId, int WorksetId)>>();

        var instanceCollector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType();

        foreach (var elem in instanceCollector)
        {
            var wsParam = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (wsParam == null || wsParam.IsReadOnly)
                continue;

            var typeId = elem.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
                continue;

            int wsId = wsParam.AsInteger();
            if (!instancesByTypeId.TryGetValue(typeId, out var list))
            {
                list = new List<(ElementId ElementId, int WorksetId)>();
                instancesByTypeId[typeId] = list;
            }
            list.Add((elem.Id, wsId));
        }

        // 2. 모델에 존재하는 모든 ElementType 수집
        var typeCollector = new FilteredElementCollector(doc)
            .OfClass(typeof(ElementType))
            .Cast<ElementType>();

        var familiesMap = new Dictionary<(string Category, string Family), FamilyWorksetItem>();

        foreach (var elementType in typeCollector)
        {
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
            var key = (categoryName, familyName);

            if (!familiesMap.TryGetValue(key, out var familyItem))
            {
                familyItem = new FamilyWorksetItem(categoryName, familyName);
                familiesMap[key] = familyItem;
            }

            // 인스턴스 정보 확인
            var instanceIds = new List<ElementId>();
            string currentWsName = "(미배치)";

            if (instancesByTypeId.TryGetValue(elementType.Id, out var instList) && instList.Count > 0)
            {
                instanceIds = instList.Select(i => i.ElementId).ToList();
                var distinctWs = instList
                    .Select(i => worksetLookup.TryGetValue(i.WorksetId, out var name) ? name : $"Workset {i.WorksetId}")
                    .Distinct()
                    .ToList();

                currentWsName = distinctWs.Count == 1 ? distinctWs[0] : "(혼합)";
            }
            else
            {
                // 인스턴스가 없을 때 타입 자체의 작업세트 확인 시도
                var typeWsParam = elementType.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (typeWsParam != null && worksetLookup.TryGetValue(typeWsParam.AsInteger(), out var name))
                {
                    currentWsName = name;
                }
            }

            var typeItem = new TypeWorksetItem(
                elementType.Id,
                typeName,
                familyItem,
                instanceIds,
                currentWsName);

            familyItem.Types.Add(typeItem);
        }

        return familiesMap.Values
            .OrderBy(f => f.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
