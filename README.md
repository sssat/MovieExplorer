# MovieExplorer

KOBIS의 국내 영화 데이터를 TMDB의 상세 정보와 결합하고, SQL Server에 축적한 흥행 데이터를 분석·시각화하는 C# WPF 데스크톱 애플리케이션입니다.

단순히 외부 API 응답을 화면에 출력하는 데 그치지 않고 **데이터 수집 → 영화 식별 및 정제 → 중복 없는 저장 → 조건별 조회 → 통계 분석 → 시각화**까지 하나의 흐름으로 구현했습니다.

## 프로젝트 개요

- 개발 형태: 개인 프로젝트
- 플랫폼: Windows 데스크톱
- 주요 목적: 외부 데이터 연계, 관계형 데이터 모델링, SQL 기반 조회·집계 경험 강화
- 데이터 기준: KOBIS가 영화 목록과 흥행 실적을 담당하고, TMDB가 포스터·줄거리·평점·장르를 보완
- 구현 범위: 영화 탐색과 흥행 분석

극장별 상영 시간표, 좌석 선택, 예매와 결제 기능은 프로젝트 범위에 포함하지 않았습니다.

## 핵심 기능

### 국내 박스오피스

- 날짜별 KOBIS 일일 박스오피스 TOP 10 조회
- 박스오피스 순위, 일일 관객 수, 누적 관객 수 표시
- 제목·줄거리 검색, 장르 필터와 정렬
- 조회 결과를 SQL Server에 중복 없이 저장하고 최신 값으로 갱신

### 개봉 예정 영화

- KOBIS 기준 향후 6개월 내 개봉 예정 장편 영화 조회
- 최대 100편까지 수집하고 10편 단위로 페이지 이동
- 같은 날짜에 수집한 결과가 있으면 저장된 정보를 우선 사용
- 제목 검색과 장르 필터 제공

### 지난 영화

- 선택 기간의 KOBIS 주간 박스오피스 TOP 10 진입작 조회
- 영화별 기간 관객 수와 누적 관객 수를 집계하여 목록 구성
- 누적·기간 관객 수, 개봉일, 평점과 제목 기준 정렬
- 저장된 과거 주차를 우선 사용하고 누락된 주차와 최근 4주만 갱신
- 10편 단위 페이지 이동과 원하는 페이지 바로 이동

### 영화 상세 및 감상 기록

- 포스터, 개봉일, 장르, 평점, 줄거리와 흥행 정보 제공
- 관심 영화 등록 및 관심 영화만 모아보기
- 개인 평점 1~5점과 최대 2,000자의 감상평 저장
- 관심 영화 검색, 평점 필터와 정렬

### 개별 영화 분석

- 기간과 영화를 선택하여 주차별 흥행 흐름 분석
- 기간 관객 수, 최고 주간 관객 수, 최고·평균 순위 표시
- 최근 관객 증감률, TOP 10 진입 기간, 최고 흥행 주 표시
- 첫 집계 대비 관객 유지율 및 순위 변화 계산
- 주간 관객 수, 누적 관객 수, 순위 변화 선 차트
- 직전 집계 대비 관객 증감률 막대 차트
- 그래프 지점에 마우스를 올리면 정확한 수치 표시
- 주차별 순위·관객 수·증감률 상세 표 제공

### 영화 정보 관리

- 보유 영화 수, 포스터 미등록 수, 상세 정보 미등록 수 확인
- 박스오피스·지난 영화·개봉 예정 영화별 보유 기간과 최근 업데이트 확인
- 각 영역의 업데이트 내역을 10건씩 페이지로 조회
- 성공·실패 상태와 추가·변경 건수 확인

## 데이터 처리 흐름

```text
KOBIS
  ├─ 일일 박스오피스 순위·관객 수
  ├─ 주간 박스오피스 순위·관객 수
  └─ 개봉 예정 영화 목록·개봉일
                    │
                    ▼
            영화 데이터 정제·식별
                    │
          제목·원제·개봉연도 비교
                    │
                    ▼
TMDB ── 포스터·줄거리·평점·장르 보완
                    │
                    ▼
         SQL Server 트랜잭션·Upsert
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
     목록·상세 조회       기간별 집계·분석
                              │
                              ▼
                       WPF 차트·요약 지표
```

## KOBIS·TMDB 영화 매칭

KOBIS에는 포스터와 상세 줄거리가 없으므로 동일 영화를 TMDB에서 검색해 정보를 보완합니다. 단순히 첫 번째 검색 결과를 사용하는 방식에서 발생할 수 있는 동명 영화 오매칭을 줄이기 위해 후보들을 점수화합니다.

1. 제목의 공백·특수문자·대소문자를 제거하고 유니코드를 정규화합니다.
2. KOBIS 제목과 원제를 TMDB의 제목·원제와 각각 비교합니다.
3. 편집 거리 기반 제목 유사도를 계산합니다.
4. 개봉연도 일치 여부와 TMDB 투표 수를 보조 점수로 반영합니다.
5. 제목 유사도 72% 이상, 종합 점수 80점 이상인 후보만 연결합니다.
6. 연도 조건으로 찾지 못하면 연도 없이 다시 검색하여 재개봉작을 보완합니다.
7. 기준을 충족하는 후보가 없으면 잘못된 정보를 연결하지 않고 상세 정보 미등록 상태로 유지합니다.

## 저장 및 조회 전략

### 박스오피스

조회할 때 KOBIS 실적과 TMDB 상세 정보를 결합합니다. `KobisMovieCode`로 영화를 식별하고 `ShowDate + MovieId` 조합으로 같은 날짜의 순위가 중복 저장되지 않도록 처리합니다.

### 개봉 예정 영화

당일 수집한 목록이 있으면 SQL Server에 저장된 정보를 바로 사용합니다. 새로운 날짜이거나 저장된 결과가 없을 때만 외부 데이터를 다시 수집하고 스냅샷을 교체합니다.

### 지난 영화

선택 기간에 필요한 주차 목록과 저장된 주차를 비교합니다. 이미 완성된 과거 주차는 다시 요청하지 않고, 누락된 주차와 변경 가능성이 있는 최근 4주만 갱신한 뒤 전체 결과는 SQL Server에서 조회합니다.

이 방식으로 외부 요청 횟수와 대기 시간을 줄이면서 최근 정보의 정확성을 유지합니다.

## 데이터베이스 설계

| 테이블 | 역할 | 주요 무결성 기준 |
| --- | --- | --- |
| `Movies` | KOBIS·TMDB 식별자와 통합 영화 기본정보 | KOBIS 코드·TMDB ID UNIQUE |
| `BoxOfficeRankings` | 날짜별 박스오피스 순위와 관객 수 | `ShowDate + MovieId` UNIQUE |
| `PastMovieRankings` | 주차별 순위와 관객 수 | `WeekEndDate + MovieId` UNIQUE |
| `UpcomingMovieCache` | 개봉 예정 영화의 최신 스냅샷 | `MovieId` PK |
| `MovieJournals` | 관심 여부, 개인 평점과 감상평 | 영화별 1건, 평점 CHECK |
| `ApiSyncLogs` | 데이터 업데이트 시작·완료 및 처리 결과 | 상태 CHECK |

애플리케이션 최초 실행 시 데이터베이스와 테이블·인덱스를 자동으로 구성합니다. 기존 테이블명이 남아 있는 환경은 현재 이름으로 자동 변경하여 이전 데이터도 이어서 사용할 수 있습니다.

## SQL 활용 내용

- PK, FK, UNIQUE, NOT NULL, CHECK 제약조건을 통한 데이터 무결성 관리
- `KobisMovieCode`, `TmdbId`, 날짜와 영화 조합을 이용한 중복 방지
- `UPDLOCK`, `HOLDLOCK`을 적용한 동시성 안전 Upsert
- 여러 영화와 순위 정보를 하나의 트랜잭션으로 처리하고 실패 시 Rollback
- 매개변수화 쿼리를 통한 안전한 데이터 접근
- `JOIN`, `GROUP BY`, `SUM`, `MIN`, `MAX`, `AVG`를 이용한 기간별 집계
- CTE를 이용한 영화별 기간 관객 수·누적 관객 수·최고 순위 산출
- 조회 조건에 맞춘 복합 인덱스로 날짜·순위 조회 성능 개선
- 업데이트 성공·실패와 추가·변경 건수를 별도 이력으로 관리

주차가 누락된 경우는 관객 수가 0명인 것이 아니라 KOBIS TOP 10 밖으로 이동한 것일 수 있습니다. 따라서 증감률은 연속된 집계 사이에서만 계산하고, 이후 다시 나타난 영화는 `TOP 10 재진입`으로 구분합니다.

## 시각화 지표

| 구분 | 표시 내용 |
| --- | --- |
| 요약 지표 | 기간 관객 수, 최고 주간 관객 수, 최고·평균 순위 |
| 흥행 지표 | 최근 관객 증감률, TOP 10 진입 기간, 최고 흥행 주, 관객 유지율 |
| 주간 관객 차트 | 집계 주차별 관객 수 변화 |
| 순위 차트 | 집계 주차별 박스오피스 순위 변화 |
| 누적 관객 차트 | 시간에 따른 누적 관객 수 성장 흐름 |
| 증감률 차트 | 직전 집계 대비 관객 증가·감소 비율 |
| 상세 표 | 주차별 순위, 주간·누적 관객 수와 증감률 |

차트는 WPF `Canvas`로 직접 구현했으며 각 지점과 막대에 마우스를 올리면 날짜와 실제 수치를 확인할 수 있습니다.

## 기술 스택

| 영역 | 기술 |
| --- | --- |
| Language | C# |
| UI | WPF, XAML |
| Architecture | MVVM, Repository Pattern |
| Runtime | .NET 9 |
| Database | Microsoft SQL Server 2022, Microsoft.Data.SqlClient |
| External Data | KOBIS Open API, TMDB API |
| Local Infrastructure | Docker, Docker Compose |
| Version Control | Git, GitHub |

## 프로젝트 구조

현재 구성 요소와 데이터 흐름은 [시스템 아키텍처](docs/architecture.md)에서 확인할 수 있습니다.

```text
MovieExplorer/
├─ compose.yaml                         # SQL Server 컨테이너 구성
├─ .env.docker.example                  # Docker 환경 변수 예시
├─ Database/
│  └─ 001_CreateMovieJournal.sql        # 수동 DB 구성 스크립트
├─ MovieExplorer.sln
└─ MovieExplorer/
   ├─ Configuration/                    # 인증 정보와 연결 문자열 로드
   ├─ Models/                           # 영화·API 응답·분석 모델
   ├─ Services/                         # API 통신, DB 접근, 동기화·분석
   ├─ ViewModels/                       # 화면 상태, Command, 조회·필터 로직
   ├─ Views/                            # XAML 화면과 UI 전용 렌더링
   ├─ .env.example                      # 애플리케이션 환경 변수 예시
   ├─ App.xaml
   ├─ MainWindow.xaml
   └─ MovieExplorer.csproj
```

화면은 `View`의 데이터 바인딩과 `ICommand`를 통해 `ViewModel`과 연결합니다. API 호출과 SQL Server 접근은 ViewModel에서 직접 구현하지 않고 기존 Service·Repository 계층에 위임합니다. Code-behind에는 초기 화면 연결, 스크롤 이동, Canvas 차트 그리기처럼 WPF UI에 종속된 동작만 남겼습니다.

## 실행 방법

### 요구 환경

- Windows 10/11
- Visual Studio 2022 또는 .NET 9 SDK
- Visual Studio 사용 시 `.NET 데스크톱 개발` 워크로드
- SQL Server 2022 또는 Docker Desktop
- KOBIS API 키와 TMDB API Read Access Token

### 1. 저장소 준비

```powershell
git clone https://github.com/sssat/MovieExplorer.git
cd MovieExplorer
Copy-Item MovieExplorer\.env.example MovieExplorer\.env
```

`MovieExplorer/.env`에 발급받은 인증 정보와 DB 연결 문자열을 입력합니다.

```dotenv
TMDB_READ_ACCESS_TOKEN=your_tmdb_api_read_access_token
KOBIS_API_KEY=your_kobis_api_key
MOVIEEXPLORER_DB_CONNECTION=Server=localhost;Database=MovieExplorer;Trusted_Connection=True;TrustServerCertificate=True;
```

`.env` 파일은 Git에서 제외됩니다. 실제 토큰이나 비밀번호를 `.env.example` 또는 커밋 기록에 포함하지 마세요.

### 2-A. 로컬 SQL Server 사용

Windows 인증으로 접속 가능한 SQL Server가 `localhost`에서 실행 중이라면 기본 연결 문자열을 그대로 사용할 수 있습니다. 애플리케이션이 `MovieExplorer` 데이터베이스와 필요한 스키마를 자동 생성합니다.

### 2-B. Docker SQL Server 사용

저장소 루트에서 Docker용 환경 파일을 준비합니다.

```powershell
Copy-Item .env.docker.example .env.docker
```

`.env.docker`의 비밀번호를 강력한 로컬 비밀번호로 변경한 뒤 실행합니다.

```powershell
docker compose --env-file .env.docker up -d
docker compose ps
```

Docker SQL Server는 호스트의 `14330` 포트를 사용합니다. `MovieExplorer/.env`에는 동일한 비밀번호를 포함한 연결 문자열을 입력합니다.

```dotenv
MOVIEEXPLORER_DB_CONNECTION=Server=localhost,14330;Database=MovieExplorer;User Id=sa;Password=your_docker_sa_password;TrustServerCertificate=True;
```

컨테이너만 중지하면 명명된 볼륨의 데이터는 유지됩니다.

```powershell
docker compose --env-file .env.docker down
```

### 3. 빌드 및 실행

Visual Studio에서 `MovieExplorer.sln`을 열고 F5를 누르거나 다음 명령을 사용합니다.

```powershell
dotnet restore
dotnet build MovieExplorer.sln
dotnet run --project MovieExplorer\MovieExplorer.csproj
```

## 주요 설계 결정

- **KOBIS 중심 목록 구성:** 한국 극장 데이터의 기준을 일관되게 유지합니다.
- **TMDB 선택적 보완:** 매칭에 실패해도 KOBIS 목록 자체는 사용할 수 있습니다.
- **DB 우선 조회:** 반복 호출을 줄이고 과거 데이터 조회 속도를 개선합니다.
- **트랜잭션 단위 저장:** 영화 기본정보와 순위 데이터가 함께 반영되도록 보장합니다.
- **화면과 데이터 접근 분리:** WPF 화면은 Repository를 통해 데이터를 사용합니다.
- **직접 구현한 차트:** 외부 차트 라이브러리 없이 데이터 좌표 계산과 툴팁 동작을 구현했습니다.

## 참고 자료

- [KOBIS Open API](https://www.kobis.or.kr/kobisopenapi/homepg/main/main.do)
- [TMDB API 문서](https://developer.themoviedb.org/docs/getting-started)
- [SQL Server Linux 컨테이너](https://learn.microsoft.com/sql/linux/quickstart-install-connect-docker)
