namespace PersonalFinanceApi.Models;

public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Kind { get; set; } = "";      // income | expense
    public string Category { get; set; } = "";
    public DateTime Date { get; set; }
    public string? Note { get; set; }
}
