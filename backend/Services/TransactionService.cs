using Npgsql;
using PersonalFinanceApi.Dtos;
using PersonalFinanceApi.Models;

namespace PersonalFinanceApi.Services;

public class TransactionService
{
    private readonly string _cs;

    public TransactionService(IConfiguration cfg)
    {
        _cs = cfg.GetConnectionString("Postgres")
              ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres");
    }

    // простая валидация
    private static (bool ok, string? error, string kind) ValidateKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return (false, "Kind is required", "");
        var k = kind.Trim().ToLowerInvariant();
        if (k is not ("income" or "expense")) return (false, "Kind must be 'income' or 'expense'", "");
        return (true, null, k);
    }

    public async Task<string> DbTestAsync()
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        return "Connected to PostgreSQL!";
    }

    public async Task<Transaction> CreateAsync(CreateTransactionDto dto)
    {
        if (dto.Amount <= 0) throw new ArgumentException("Amount must be > 0");
        if (string.IsNullOrWhiteSpace(dto.Category)) throw new ArgumentException("Category is required");

        var (ok, err, kind) = ValidateKind(dto.Kind);
        if (!ok) throw new ArgumentException(err);

        var date = dto.Date ?? DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
INSERT INTO transactions (amount, kind, category, date, note)
VALUES (@amount, @kind, @category, @date, @note)
RETURNING id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("amount", dto.Amount);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("category", dto.Category.Trim());
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("note", (object?)dto.Note ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        if (result is null)
            throw new InvalidOperationException("Failed to get generated id");

        var id = (int)result;

        return new Transaction
        {
            Id = id,
            Amount = dto.Amount,
            Kind = kind,
            Category = dto.Category.Trim(),
            Date = date,
            Note = dto.Note
        };
    }

    public async Task<List<Transaction>> GetAllAsync(string? kind, string? category)
    {
        var filters = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(kind))
        {
            var (ok, err, k) = ValidateKind(kind);
            if (!ok) throw new ArgumentException(err);
            filters.Add("kind = @kind");
            parameters.Add(new NpgsqlParameter("kind", k));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add("LOWER(category) = LOWER(@category)");
            parameters.Add(new NpgsqlParameter("category", category.Trim()));
        }

        var where = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);

        var sql = $@"
SELECT id, amount, kind, category, date, note
FROM transactions
{where}
ORDER BY id;";

        var result = new List<Transaction>();

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new Transaction
            {
                Id = reader.GetInt32(0),
                Amount = reader.GetDecimal(1),
                Kind = reader.GetString(2),
                Category = reader.GetString(3),
                Date = reader.GetDateTime(4),
                Note = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return result;
    }

    public async Task<Transaction?> GetByIdAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
SELECT id, amount, kind, category, date, note
FROM transactions
WHERE id = @id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Transaction
        {
            Id = reader.GetInt32(0),
            Amount = reader.GetDecimal(1),
            Kind = reader.GetString(2),
            Category = reader.GetString(3),
            Date = reader.GetDateTime(4),
            Note = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    public async Task<Transaction?> UpdateAsync(int id, UpdateTransactionDto dto)
    {
        var existing = await GetByIdAsync(id);
        if (existing is null) return null;

        if (dto.Amount is not null)
        {
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be > 0");
            existing.Amount = dto.Amount.Value;
        }

        if (dto.Kind is not null)
        {
            var (ok, err, k) = ValidateKind(dto.Kind);
            if (!ok) throw new ArgumentException(err);
            existing.Kind = k;
        }

        if (dto.Category is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.Category)) throw new ArgumentException("Category is required");
            existing.Category = dto.Category.Trim();
        }

        if (dto.Date is not null) existing.Date = dto.Date.Value;
        if (dto.Note is not null) existing.Note = dto.Note; // null очистит

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
UPDATE transactions
SET amount=@amount, kind=@kind, category=@category, date=@date, note=@note
WHERE id=@id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("amount", existing.Amount);
        cmd.Parameters.AddWithValue("kind", existing.Kind);
        cmd.Parameters.AddWithValue("category", existing.Category);
        cmd.Parameters.AddWithValue("date", existing.Date);
        cmd.Parameters.AddWithValue("note", (object?)existing.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"DELETE FROM transactions WHERE id=@id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<object> GetBalanceAsync()
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
SELECT
  COALESCE(SUM(CASE WHEN kind='income' THEN amount END), 0) AS income,
  COALESCE(SUM(CASE WHEN kind='expense' THEN amount END), 0) AS expense
FROM transactions;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        await reader.ReadAsync();
        var income = reader.GetDecimal(0);
        var expense = reader.GetDecimal(1);
        var balance = income - expense;

        return new { income, expense, balance };
    }
}
