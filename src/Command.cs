using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RoBIM.Feature;

[Transaction(TransactionMode.Manual)]
public sealed class Command : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        try
        {
            var uidoc = data.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("타입별 작업 세트 관리", "활성화된 Revit 프로젝트 문서를 열어주세요.");
                return Result.Cancelled;
            }

            var doc = uidoc.Document;
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("타입별 작업 세트 관리", "패밀리 편집기가 아닌 일반 프로젝트 모델 문서에서 실행해 주세요.");
                return Result.Cancelled;
            }

            if (!doc.IsWorkshared)
            {
                TaskDialog.Show(
                    "타입별 작업 세트 관리",
                    "현재 프로젝트 문서는 작업 공유(Worksharing) 모델이 아닙니다.\n" +
                    "작업 세트를 사용하고 관리하려면 Revit의 [공동 작업(Collaborate)] 탭에서 작업 공유를 먼저 활성화해 주세요.");
                return Result.Cancelled;
            }

            var window = new WorksetManagerWindow(doc);
            new WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            window.ShowDialog();

            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("타입별 작업 세트 관리 오류", ex.Message);
            return Result.Failed;
        }
    }
}
