# MovieExplorer 시스템 아키텍처

```mermaid
flowchart LR
    User["사용자"]

    subgraph Desktop["MovieExplorer · WPF 데스크톱 애플리케이션"]
        direction LR
        View["View · XAML<br/>화면 표시 · 데이터 바인딩"]
        ViewModel["ViewModel<br/>화면 상태 · ICommand<br/>조회 · 필터 · 페이지 이동"]
        Model["Model<br/>영화 · 순위 · 감상 기록<br/>분석 데이터"]
        Service["Service<br/>외부 API 호출<br/>영화 식별 · 정보 결합"]
        Repository["Repository<br/>ADO.NET · 직접 SQL<br/>트랜잭션 · DB 우선 조회"]

        View <-->|"Binding · Command"| ViewModel
        ViewModel <-->|"화면 데이터"| Model
        ViewModel -->|"영화 정보 요청"| Service
        ViewModel -->|"저장 · 조회 · 분석 요청"| Repository
        Service -->|"API 응답 변환"| Model
        Repository -->|"조회 결과 변환"| Model
    end

    subgraph External["외부 영화 데이터"]
        KOBIS["KOBIS API<br/>박스오피스 순위 · 관객 수<br/>개봉 정보"]
        TMDB["TMDB API<br/>포스터 · 줄거리<br/>장르 · 평점"]
    end

    Database[("SQL Server<br/>영화 · 순위 · 감상 기록<br/>업데이트 이력")]

    User -->|"검색 · 필터 · 기록 입력"| View
    View -->|"영화 목록 · 상세 · 통계"| User

    Service <-->|"순위 · 관객 · 개봉 데이터"| KOBIS
    Service <-->|"상세 영화 데이터"| TMDB
    Repository <-->|"SELECT · INSERT · UPDATE"| Database

    classDef user fill:#EAF3FF,stroke:#4D83C4,stroke-width:2px,color:#172033
    classDef view fill:#EAF8EF,stroke:#3A9B62,stroke-width:2px,color:#172033
    classDef viewmodel fill:#FFF8E5,stroke:#D89B20,stroke-width:2px,color:#172033
    classDef model fill:#FFF1E8,stroke:#D97835,stroke-width:2px,color:#172033
    classDef service fill:#F4ECFF,stroke:#8B5FD3,stroke-width:2px,color:#172033
    classDef repository fill:#EDEBFF,stroke:#6657C7,stroke-width:2px,color:#172033
    classDef external fill:#F4F6F8,stroke:#778391,stroke-width:2px,color:#172033
    classDef database fill:#FFF0F0,stroke:#DF4B4B,stroke-width:2px,color:#172033

    class User user
    class View view
    class ViewModel viewmodel
    class Model model
    class Service service
    class Repository repository
    class KOBIS,TMDB external
    class Database database

    style Desktop fill:#FAFAFF,stroke:#7367D8,stroke-width:2px
    style External fill:#F8FAFC,stroke:#778391,stroke-width:2px
```

## 핵심 흐름

1. 사용자의 입력은 XAML View에서 Command와 양방향 바인딩을 통해 ViewModel로 전달됩니다.
2. ViewModel은 외부 영화 정보가 필요하면 Service를, 저장·조회·통계가 필요하면 Repository를 호출합니다.
3. Service는 KOBIS 영화와 TMDB 상세 정보를 검증·결합하여 Model로 반환합니다.
4. Repository는 ADO.NET과 매개변수화 SQL로 SQL Server 데이터를 처리합니다.
5. ViewModel이 반환된 데이터와 화면 상태를 갱신하면 View가 자동으로 변경됩니다.

