using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Diagnostics;

namespace MoneyConverter;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private readonly HttpClient _httpClient = new();
    private DateTime _selectedDate = DateTime.Today;
    private string _rateDateText;
    private Dictionary<string, CurrencyRate> _ratesCache = new();


    public event PropertyChangedEventHandler PropertyChanged;

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetProperty(ref _selectedDate, value))
            {
                _ = LoadRatesAsync();
            }
        }
    }

    public string RateDateText
    {
        get => _rateDateText;
        set => SetProperty(ref _rateDateText, value);
    }

    public ObservableCollection<string> Currencies { get; } = new();

    private string _sourceCurrency;
    public string SourceCurrency
    {
        get => _sourceCurrency;
        set
        {
            if (SetProperty(ref _sourceCurrency, value))
            {
                if (value == "JPY")
                {
                    SourceAmount = "100";
                }
                CalculateAmount();
            }
        }
    }

    private string _targetCurrency;
    public string TargetCurrency
    {
        get => _targetCurrency;
        set
        {
            if (SetProperty(ref _targetCurrency, value))
                CalculateAmount();
        }
    }

    private bool _isUpdatingAmounts; //флаг на проверку, изменилось ли поле целевой валюты

    private string _sourceAmount;
    public string SourceAmount
    {
        get => _sourceAmount;
        set
        {
            if (SetProperty(ref _sourceAmount, value))
            {
                if (decimal.TryParse(value, out var amount))
                {
                    _sourceDecimalAmount = amount;
                    CalculateAmount();
                }
                else
                {
                    _sourceDecimalAmount = 0;
                }
            }
        }
    }

    private string _targetAmount;
    public string TargetAmount
    {
        get => _targetAmount;
        set
        {
            if (SetProperty(ref _targetAmount, value) && !_isUpdatingAmounts)
            {
                if (decimal.TryParse(value, out var amount))
                {
                    _isUpdatingAmounts = true; // Устанавливаем флаг, чтобы избежать циклического вызова
                    _targetDecimalAmount = amount;
                    CalculateAmount();
                    _isUpdatingAmounts = false; // Сбрасываем флаг
                }
                else
                {
                    _targetDecimalAmount = 0;
                }
            }
        }
    }


    private decimal _sourceDecimalAmount;
    private decimal _targetDecimalAmount;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;
        LoadPreferences();
        _ = LoadRatesAsync();
    }

    private async Task LoadRatesAsync()
    {
        DateTime date = SelectedDate;

        while (true)
        {
            var rates = await GetRatesAsync(date);
            if (rates != null)
            {
                _ratesCache = rates;
                RateDateText = $"{date:dd.MM.yyyy}";

                UpdateCurrencies();
                CalculateAmount();

                SavePreferences();
                break;
            }
            date = date.AddDays(-1);
            if (date < DateTime.Today.AddYears(-1))
            {
                RateDateText = "Курсы недоступны для выбранной даты";
                break;
            }
        }
    }

    private async Task<Dictionary<string, CurrencyRate>> GetRatesAsync(DateTime date)
    {
        string url = date.Date == DateTime.Today
            ? "https://www.cbr-xml-daily.ru/daily_json.js"
            : $"https://www.cbr-xml-daily.ru/archive/{date:yyyy'%2F'MM'%2F'dd}/daily_json.js"; // yyyy/MM/dd

        try
        {
            var response = await _httpClient.GetFromJsonAsync<JsonElement>(url);
            if (response.TryGetProperty("Valute", out var valute))
            {
                var rates = valute.Deserialize<Dictionary<string, CurrencyRate>>() ?? new();

                foreach (var currency in rates.Values)
                {
                    rates["RUB"] = new CurrencyRate
                    {
                        CharCode = "RUB",
                        Name = "Российский рубль",
                        Value = 1,
                        Nominal = 1
                    };

                    return rates;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"Ошибка загрузки данных: {ex.Message}");
        }

        return null;
    }

    private void UpdateCurrencies()
    {
        var previousSourceCurrency = SourceCurrency;
        var previousTargetCurrency = TargetCurrency;

        Currencies.Clear();
        foreach (var rate in _ratesCache.Values)
        {
            Currencies.Add(rate.CharCode);
        }

        if (Currencies.Contains(previousSourceCurrency))
            SourceCurrency = previousSourceCurrency;
        if (Currencies.Contains(previousTargetCurrency))
            TargetCurrency = previousTargetCurrency;
    }

    public void CalculateAmount()
    {
        if (string.IsNullOrEmpty(SourceCurrency) || string.IsNullOrEmpty(TargetCurrency) ||
            !_ratesCache.ContainsKey(SourceCurrency) || !_ratesCache.ContainsKey(TargetCurrency))
        {
            return;
        }

        var sourceRate = _ratesCache[SourceCurrency];
        var targetRate = _ratesCache[TargetCurrency];
        if(_isUpdatingAmounts == true) //проверяем флаг если он истинна, значит мы должны поменять исходную валюту
        {
            decimal result = (_targetDecimalAmount * targetRate.Value / targetRate.Nominal) * sourceRate.Nominal / sourceRate.Value;

            SourceAmount = result.ToString("F2");
        }
        else //в ином случае меняем исходную
        {
            decimal result = (_sourceDecimalAmount * sourceRate.Value / sourceRate.Nominal) * targetRate.Nominal / targetRate.Value;

            TargetAmount = result.ToString("F2");
        }
    }


    private void LoadPreferences()
    {
        SelectedDate = Preferences.Get("SelectedDate", DateTime.Today);
        SourceCurrency = Preferences.Get("SourceCurrency", "USD");
        TargetCurrency = Preferences.Get("TargetCurrency", "EUR");
        SourceAmount = Preferences.Get("SourceAmount", "1");
    }

    private void SavePreferences()
    {
        Preferences.Set("SelectedDate", SelectedDate);
        Preferences.Set("SourceCurrency", SourceCurrency);
        Preferences.Set("TargetCurrency", TargetCurrency);
        Preferences.Set("SourceAmount", SourceAmount);
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class CurrencyRate
{
    public string CharCode { get; set; }
    public string Name { get; set; }
    public decimal Value { get; set; }
    public decimal Nominal { get; set; }

    public decimal GetRate(decimal amount)
    {
        return amount * Value / Nominal;
    }
}