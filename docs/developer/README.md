# 타입별 작업 세트 관리 - 개발자 설명서

## 1. 아키텍처 개요

본 모듈은 RoBIM Revit Native 모듈 규약(1-Button-1-Repository)에 따라 독립된 단일 명령으로 구성되어 있습니다.

- **대상 호스트**: Autodesk Revit 2026.5+ (.NET 10 / net10.0-windows)
- **모듈 ID**: `workset-manager`
- **명령 ID**: `run`
- **패키지 ID**: `robim-workset-manager`
- **의존 라이브러리**: `RoBIM.Common [0.1.0]`, `RoBIM.Wpf [0.1.0]`

---

## 2. 핵심 소스 코드 구조

- `src/Command.cs`:
  - `IExternalCommand` 구현체.
  - 활성 프로젝트 문서 확인 및 패밀리 편집기 차단.
  - `doc.IsWorkshared` 검증을 통해 작업 공유 모델이 아닌 경우 안전하게 중단.
  - `WorksetManagerWindow` 모달 다이얼로그 생성 및 `MainWindowHandle` 소유권 지정.
- `src/WorksetModels.cs`:
  - `WorksetInfo`: 작업 세트 ID 및 이름 래퍼.
  - `FamilyWorksetItem`: 패밀리 레벨 항목. `TargetWorkset` 변경 시 하위 모든 `TypeWorksetItem`에 자동 전파(Cascade Update).
  - `TypeWorksetItem`: 개별 타입 레벨 항목. 인스턴스 ID 목록, 현재 작업세트 요약 및 변경 대상 작업세트 관리.
- `src/WorksetCollector.cs`:
  - `FilteredWorksetCollector`를 통한 `UserWorkset` 수집.
  - `FilteredElementCollector`를 통한 모델 인스턴스 및 `ElementType` 수집, `ELEM_PARTITION_PARAM` 파라미터 매핑.
- `src/WorksetManagerWindow.cs`:
  - WPF 코드 비하인드 기반 UI.
  - 마스터-디테일 레이아웃: 좌측 패밀리 DataGrid, 우측 세부 타입 DataGrid.
  - 실시간 필터링(카테고리, 검색어, 배치 여부) 및 일괄 지정 툴바.
  - 단일 트랜잭션(`using var trans = new Transaction(doc, "타입별 작업 세트 일괄 변경")`)을 통한 안전한 모델 수정 및 1-클릭 Undo 보장.

---

## 3. 빌드 및 패키징

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -CommonFeed ..\RoBIM-Libraries\artifacts\packages
```

- 출력물: `artifacts/<version>/RoBIM.Feature.WorksetManager-Revit2026.zip` 및 `.sha256` 체크섬.
- 번들 구조: `RoBIM.Feature.WorksetManager.bundle/Contents/2026/` 내에 모듈 DLL 및 `package/robim-module.json`, `package/robim-release.json`, `icon.png` 포함.
