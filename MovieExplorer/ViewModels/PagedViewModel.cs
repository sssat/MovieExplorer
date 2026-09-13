using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MovieExplorer.ViewModels;

public abstract class PagedViewModel<T> : ObservableObject
{
    protected const int PageSize = 10;
    private int currentPage = 1;
    private int totalItemCount;

    public ObservableCollection<T> Items { get; } = [];
    public int CurrentPage
    {
        get => currentPage;
        set
        {
            if (SetProperty(ref currentPage, value))
            {
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(ResultLabel));
            }
        }
    }

    public int TotalItemCount
    {
        get => totalItemCount;
        protected set
        {
            if (SetProperty(ref totalItemCount, value))
            {
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(ResultLabel));
                OnPropertyChanged(nameof(HasItems));
            }
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItemCount / (double)PageSize));
    public string ResultLabel => TotalItemCount == 0 ? "" : $"총 {TotalItemCount}편 · {CurrentPage}/{TotalPages} 페이지";
    public bool HasItems => TotalItemCount > 0;
    public int ItemsPerPage => PageSize;
    public ICommand ChangePageCommand { get; }

    protected PagedViewModel()
    {
        ChangePageCommand = new RelayCommand<int>(page =>
        {
            CurrentPage = Math.Clamp(page, 1, TotalPages);
            ApplyCurrentPage();
        });
    }

    protected void SetPage(IEnumerable<T> filtered)
    {
        List<T> values = filtered.ToList();
        TotalItemCount = values.Count;
        CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);
        Items.Clear();
        foreach (T item in values.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            Items.Add(item);
    }

    protected abstract void ApplyCurrentPage();
}
