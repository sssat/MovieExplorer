# MovieExplorer

KOBIS 국내 박스오피스 TOP 10에 TMDB의 영화 정보를 결합하고 MSSQL에 저장·분석하는 C# WPF 데스크톱 애플리케이션입니다.

영화 목록과 흥행 통계는 KOBIS TOP 10을 기준으로 하며, KOBIS에 없는 포스터·줄거리·평점·장르만 TMDB에서 보완합니다. 외부 API 데이터를 수집·정제하여 MSSQL에 누적하고, SQL로 기간별 흥행 지표를 분석한 뒤 WPF의 차트와 표로 시각화하는 것을 핵심 목표로 합니다.

> 현재 API 연동, 영화 목록·상세 화면, MSSQL 기반 관심 영화·감상 기록, 박스오피스 데이터 동기화와 기간별 통계·시각화까지 구현되어 있습니다.

## 프로젝트 목표

- C#과 WPF를 활용한 Windows 데스크톱 프로그램 개발
- 서로 다른 외부 API 데이터의 수집·정제·매칭
- 수집한 데이터를 MSSQL에 누적하는 ETL 형태의 데이터 처리 흐름 구현
- MSSQL 기반 데이터 모델링과 CRUD 구현
- PK, FK, UNIQUE 제약조건을 통한 데이터 무결성 보장
- JOIN, GROUP BY, CTE, 윈도우 함수를 활용한 박스오피스 통계 제공
- SQL 분석 결과를 차트와 요약 지표로 시각화
- 트랜잭션과 동기화 이력을 통한 안정적인 데이터 처리

극장별 상영 시간표, 좌석 선택, 예매 및 결제 기능은 구현 범위에 포함하지 않습니다.

## 주요 기능 및 진행 상태

| 구분 | 기능 | 상태 |
| --- | --- | --- |
| 국내 박스오피스 | KOBIS 일별 TOP 10 및 관객 수 조회 | 구현 완료 |
| 데이터 결합 | KOBIS TOP 10에 TMDB 포스터·줄거리·평점·장르 연결 | 구현 완료 |
| 영화 탐색 | 제목·줄거리 검색, 장르 필터 | 구현 완료 |
| 페이지 이동·정렬 | 목록별 10편 페이지 이동 및 화면별 정렬·평점 필터 | 구현 완료 |
| 예외 처리 | 로딩, 빈 결과, 인증 정보 누락, API 오류 표시 | 구현 완료 |
| 개봉 예정 영화 | KOBIS에서 향후 6개월 내 개봉 예정 장편을 최대 100편 조회하고 일별 DB 캐시 사용 | 구현 완료 |
| 지난 영화 | 선택 기간의 KOBIS 박스오피스 진입작을 조회하고 10편씩 페이징 | 구현 완료 |
| 영화 상세 | 포스터·개봉일·평점·장르·줄거리·흥행 정보 화면 | 구현 완료 |
| 관심 영화·감상 기록 | 관심 여부, 1~5점 개인 평점, 감상평 저장 및 관심 영화 모아보기 | 구현 완료 |
| 데이터 동기화 | KOBIS 일별·주간 실적과 TMDB 결합 결과를 MSSQL에 중복 없이 Upsert | 구현 완료 |
| 개별 영화 분석 | 기간과 영화를 선택해 주간 관객 수·누적 관객 수·순위 변동 분석 | 구현 완료 |
| 데이터 시각화 | 개별 영화 분석 결과를 선 차트·요약 지표·주차별 표로 표시 | 구현 완료 |
| 데이터 관리 | 데이터별 저장 건수·보유 기간·누락 정보와 최근 동기화 이력 확인 | 구현 완료 |

## 프로젝트 핵심 흐름

이 프로젝트는 API 조회 화면 자체보다 외부 데이터를 내부 데이터베이스로 가져와 업무에 사용할 수 있는 정보로 변환하는 과정에 중점을 둡니다.

1. KOBIS에서 기준 날짜의 박스오피스 TOP 10과 관객 수를 수집합니다.
2. KOBIS 영화목록 API에서 향후 6개월 내 개봉 예정 장편을 수집합니다.
3. 선택 기간의 KOBIS 데이터를 수집하고 영화별 최대 누적 관객 수 기준으로 지난 영화 목록을 구성합니다.
4. 영화 제목과 개봉 연도로 TMDB 데이터를 매칭하여 포스터·줄거리·평점·장르를 보완합니다.
5. 수집 결과를 검증하고 영화 기본정보와 일별·주간 실적으로 분리해 MSSQL에 저장합니다.
6. 기간과 영화를 조건으로 주간 관객 수, 누적 관객 수, 최고·평균 순위와 순위 변화를 계산합니다.
7. 계산 결과를 WPF 선 차트, 주차별 표 및 요약 카드로 시각화합니다.
8. 동기화 성공·실패 내역을 기록하여 데이터 처리 상태를 추적합니다.

## 데이터 처리 구조

```text
KOBIS API ── 박스오피스 순위·관객 수 ─┐
                                      ├─ 데이터 정제·영화 매칭
TMDB API ── 포스터·줄거리·평점·장르 ──┘
                                                ↓
                                           MSSQL 저장
                                                ↓
                                  저장 프로시저·SQL 집계 및 분석
                                                ↓
                                      WPF 차트·표·요약 지표
```

KOBIS 공식 Open API에는 포스터가 없기 때문에 영화 제목과 개봉 연도를 기준으로 TMDB 데이터를 검색하여 결합합니다. 제목만으로 연결할 때 발생할 수 있는 동명 영화 오매칭을 줄이기 위해 추후 개봉일과 추가 상세정보까지 비교할 예정입니다.

박스오피스 조회 결과는 `Movies`, `BoxOfficeRankings`에 저장하고, 지난 영화 조회에서 수집한 기간별 실적은 `Movies`, `PastMovieRankings`에 트랜잭션으로 Upsert합니다. 동일 영화는 KOBIS 영화 코드로 식별하며, 지난 영화 실적은 `WeekEndDate + MovieId` UNIQUE 제약조건으로 중복을 방지합니다. 지난 영화 재조회 시 저장된 과거 주차는 MSSQL에서 바로 읽고, 누락된 주차와 최근 4주만 KOBIS에서 갱신합니다. 통계 화면은 `PastMovieRankings`에 동기화된 데이터를 기간과 영화 조건으로 조회하고, 선택 영화의 실적을 분석합니다.

## 화면 구성

### 사용자 화면

- 국내 박스오피스
- 국내 박스오피스 영화 상세정보
- KOBIS 개봉 예정 영화
- 기간별 지난 영화 조회·검색 및 상세정보
- 영화 검색 및 상세정보
- 관심 영화 및 개인 감상 기록
- 관심 영화 검색 및 모아보기
- 기간별 분석 영화 선택
- 개별 영화의 관객 수·순위 변화 차트

### 데이터 관리 화면

관리 기능은 별도의 대규모 관리자 시스템으로 확장하지 않고, API 데이터 관리에 필요한 한 화면으로 구성합니다.

- 연결된 MSSQL 서버와 데이터베이스 확인
- 영화·박스오피스·지난 영화·개봉 예정 캐시의 저장 건수와 보유 기간 확인
- 포스터와 TMDB 연결 정보가 없는 데이터 건수 확인
- 박스오피스·지난 영화·개봉 예정 데이터의 동기화 이력 통합 기록
- 동기화별 수신·신규·갱신 건수 및 성공·실패 상태 확인
- 최근 동기화 이력과 오류 메시지를 10건씩 페이지 조회

## MSSQL 설계 계획

| 테이블 | 용도 |
| --- | --- |
| `Movies` | KOBIS·TMDB 식별자와 통합 영화 기본정보 |
| `BoxOfficeRankings` | 박스오피스 화면의 날짜별 순위, 일일·누적 관객 데이터 (`ShowDate + MovieId` UNIQUE) |
| `PastMovieRankings` | 지난 영화·통계 화면의 기간별 순위와 관객 데이터 (`WeekEndDate + MovieId` UNIQUE) |
| `UpcomingMovieCache` | 개봉 예정작의 일별 조회 스냅샷과 영화 연결 정보 |
| `Genres` | 장르 기준정보 |
| `MovieGenres` | 영화와 장르의 다대다 관계 |
| `MovieJournals` | 영화별 관심 여부, 개인 평점과 감상평 |
| `ApiSyncLogs` | API 동기화 시작·종료, 성공·실패 및 신규·갱신 건수 |
| `ApiSyncErrors` | 동기화 실패 데이터와 오류 내용 |

영화 기본정보와 매일 변경되는 박스오피스 실적을 분리해 중복 저장을 방지합니다. 동일 영화의 동일 기준일 데이터에는 UNIQUE 제약조건을 적용하고, 영화 코드와 날짜 등 자주 조회하는 컬럼에는 인덱스를 적용할 예정입니다.

## SQL 활용 계획

- 영화 기본정보 및 박스오피스 데이터 Upsert
- 트랜잭션을 적용한 일괄 동기화와 실패 시 Rollback
- PK·FK·NOT NULL·UNIQUE·CHECK 제약조건을 이용한 무결성 관리
- 저장 프로시저를 통한 조회 및 데이터 변경 로직 관리
- JOIN과 GROUP BY를 이용한 기간별·장르별 집계
- CTE와 `LAG()` 등의 윈도우 함수를 이용한 전일 대비 순위 변화 계산
- 실행 계획 확인과 인덱스 적용을 통한 조회 성능 개선

예정된 주요 조회 결과는 다음과 같습니다.

- 기간별 영화 관객 수 추이
- 전일 대비 순위 상승·하락
- 기간 내 평균 순위와 누적 관객 수
- 장르별 관객 수 분포
- 신규 진입 영화와 TOP 10 유지 일수

## 데이터 시각화

SQL에서 집계한 결과를 WPF에서 다음과 같이 시각화합니다.

| 시각화 | 표시 내용 | 활용 SQL |
| --- | --- | --- |
| 요약 카드 | 선택 영화의 기간·누적·최고 주간 관객 수와 최고·평균 순위 | SUM, MAX, MIN, AVG |
| 관객 수 선 차트 | 선택 영화의 주차별 관객 수 변화 | 기간·영화 조건, ORDER BY |
| 순위 선 차트 | 선택 영화의 주차별 박스오피스 순위 변화 | 기간·영화 조건, ORDER BY |
| 데이터 표 | 집계 주차별 순위, 주간·누적 관객 수 | JOIN, 기간·영화 조건 |

차트만 보여주는 데 그치지 않고 동일한 집계 결과를 표로도 제공하여 실제 수치와 계산 결과를 확인할 수 있게 구성합니다.

## 기술 스택

- Language: C#
- UI: WPF, XAML
- Runtime: .NET 9
- Database: Microsoft SQL Server, Microsoft.Data.SqlClient
- API: KOBIS Open API, TMDB API
- Version Control: Git, GitHub

## 프로젝트 구조

```text
MovieExplorer/
├─ MovieExplorer.sln
├─ README.md
├─ Database/            # MSSQL 스키마 생성 스크립트
└─ MovieExplorer/
   ├─ Configuration/    # 로컬 인증 정보 로드
   ├─ Models/           # API 및 화면 데이터 모델
   ├─ Services/         # KOBIS·TMDB API 통신 및 MSSQL 데이터 접근
   ├─ Views/            # 영화 목록·상세·관심 영화·통계 UserControl
   ├─ App.xaml          # 애플리케이션 시작점과 공통 리소스
   ├─ MainWindow.xaml   # 영화 목록 화면 UI
   └─ MainWindow.xaml.cs
```

영화 화면과 데이터 접근 로직을 분리하여 UI는 저장 방식에 직접 의존하지 않도록 구성합니다.

## 실행 방법

### 1. 요구 환경

- Windows
- Visual Studio 2022
- .NET 데스크톱 개발 워크로드
- .NET 9 SDK
- Microsoft SQL Server 2022 이상(Windows 인증)

로컬 SQL Server를 직접 설치하는 대신 Docker Desktop과 Docker Compose를 사용할 수 있습니다.

### 2. API 인증 설정

`MovieExplorer/.env.example`을 `MovieExplorer/.env`로 복사한 뒤 발급받은 인증 정보를 입력합니다.

```dotenv
TMDB_READ_ACCESS_TOKEN=본인의_TMDB_API_Read_Access_Token
KOBIS_API_KEY=본인의_KOBIS_인증키
MOVIEEXPLORER_DB_CONNECTION=Server=localhost;Database=MovieExplorer;Trusted_Connection=True;TrustServerCertificate=True;
```

`.env`는 Git에서 제외됩니다. `.env.example`에는 실제 인증 정보를 입력하지 않습니다. 운영체제 환경 변수에 같은 이름의 값이 있으면 환경 변수 값을 우선 사용합니다.

DB 연결 문자열을 생략하면 위의 로컬 기본값을 사용합니다. 앱에서 최초 DB 기능을 실행할 때 `MovieExplorer` 데이터베이스와 필요한 테이블을 자동 생성합니다. 수동으로 구성하려면 `Database/001_CreateMovieJournal.sql`을 실행할 수 있습니다.

### 3. Docker로 MSSQL 실행하기 (선택)

현재 PC의 로컬 SQL Server와 포트가 겹치지 않도록 Docker SQL Server는 `localhost,14330`을 사용합니다.

1. 저장소 루트의 `.env.docker.example`을 `.env.docker`로 복사합니다.
2. `.env.docker`의 `MSSQL_SA_PASSWORD`를 본인만 사용할 강력한 비밀번호로 변경합니다.
3. 다음 명령으로 컨테이너를 실행합니다.

```powershell
Copy-Item .env.docker.example .env.docker
docker compose --env-file .env.docker up -d
docker compose ps
```

4. `MovieExplorer/.env`의 DB 연결 문자열에 같은 비밀번호를 입력합니다.

```dotenv
MOVIEEXPLORER_DB_CONNECTION=Server=localhost,14330;Database=MovieExplorer;User Id=sa;Password=본인의_MSSQL_SA_PASSWORD;TrustServerCertificate=True;
```

컨테이너를 중지할 때는 다음 명령을 사용합니다. 명명된 볼륨은 유지되므로 다시 실행해도 DB 데이터가 남아 있습니다.

```powershell
docker compose --env-file .env.docker down
```

Docker DB와 기존 `localhost`의 로컬 DB는 서로 다른 데이터베이스입니다. 기존 로컬 DB 데이터가 Docker로 자동 복사되지는 않습니다.

### 4. 프로그램 실행

1. Visual Studio에서 `MovieExplorer.sln`을 엽니다.
2. 솔루션을 빌드합니다.
3. F5를 눌러 실행합니다.

## 개발 로드맵

1. KOBIS·TMDB API 연동 및 박스오피스 목록 구현 — 완료
2. KOBIS 개봉 예정 목록과 TMDB 부가정보 결합 화면 구현 — 완료
3. KOBIS 기반 지난 영화 조회와 TMDB 부가정보 결합 — 완료
4. MSSQL 관심 영화·감상 기록 테이블 및 CRUD 구현 — 완료
5. KOBIS·TMDB 결합 데이터 Upsert와 동기화 이력 저장 — 완료
6. DB 데이터를 이용한 영화·기간 조건 조회 구현
7. SQL 기반 박스오피스 분석 쿼리와 저장 프로시저 구현
8. 분석 결과 차트·요약 카드·데이터 표 구현
9. 최소 데이터 관리 화면 구현
10. 테스트, 쿼리 성능 개선 및 문서화

## 포트폴리오 핵심 내용

외부 API 데이터를 단순 출력하지 않고 MSSQL에 정규화하여 저장하고, 서로 다른 KOBIS·TMDB 데이터를 하나의 영화 정보로 통합하는 과정을 구현합니다. 데이터 동기화 시 트랜잭션과 제약조건으로 정합성을 유지하며, 저장 프로시저·JOIN·CTE·윈도우 함수로 박스오피스 지표를 분석하고 그 결과를 WPF 차트와 표로 시각화합니다.

관리 기능은 API 동기화와 처리 이력을 확인하는 최소 화면으로 제한하고, 프로젝트의 중심을 데이터 모델링, SQL 처리, 사용자 검색 및 통계 기능에 둡니다.

## 참고 문서

- [KOBIS Open API](https://www.kobis.or.kr/kobisopenapi/homepg/main/main.do)
- [TMDB 상영 중 API](https://developer.themoviedb.org/reference/movie-now-playing-list)
- [TMDB 개봉 예정 API](https://developer.themoviedb.org/reference/movie-upcoming-list)
- [TMDB 지역 지원](https://developer.themoviedb.org/docs/region-support)
- [TMDB 사용 및 출처 표기 안내](https://developer.themoviedb.org/docs/faq)
