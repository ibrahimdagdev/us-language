using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed partial class Evaluator
{
    internal void RequireDatabase(TextSpan span)
    {
        if (_scope?.Closed == true)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ScopeClosed, "Kapatılan kapsamdan veritabanı kullanılamaz.", span.Start, span.Length);
        }

        if (_database is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, "Veritabanı yok.", span.Start, span.Length);
        }
    }

    private void ExitTransaction(BoundNode node)
    {
        if (_tx != TxState.Active)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", node.Span.Start, node.Span.Length);
        }

        if (node.Operator == TokenKind.Onayla)
        {
            _database?.Commit();
        }
        else
        {
            _database?.Rollback();
        }

        _tx = TxState.Closed;
    }

    private void FinishTransaction(bool commit)
    {
        if (_tx != TxState.Active)
        {
            return;
        }

        if (commit)
        {
            _database?.Commit();
        }
        else
        {
            _database?.Rollback();
        }

        _tx = TxState.Closed;
    }

    private async ValueTask<UsValue> TableCall(BoundNode node)
    {
        RequireDatabase(node.Span);
        var receiver = await Evaluate(node.Children[0]).ConfigureAwait(false);
        if (receiver.Reference is not UsTableQuery query)
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, "Tablo sorgusu yok.", node.Span.Start, node.Span.Length);
        }

        var name = node.Constant ?? string.Empty;
        if (name is "filtrele" or "sırala" or "tsırala" or "birlikte")
        {
            var argument = node.Children.Length > 1 ? node.Children[1] : null;
            var include = argument?.Symbol as FieldSymbol;
            var next = query.With(name, name == "birlikte" ? null : argument, include, name == "tsırala");
            return UsValue.FromReference(next);
        }

        if (name == "tümü")
        {
            var list = await Materialize(query, node.Span).ConfigureAwait(false);
            query.Database.LastPlan = Plan(query, "tümü");
            return Done(UsValue.FromReference(list));
        }

        if (name is "ekle" or "güncelle" or "sil")
        {
            if (node.Children.Length < 2)
            {
                throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, "Kayıt yok.", node.Span.Start, node.Span.Length);
            }

            var value = await Evaluate(node.Children[1]).ConfigureAwait(false);
            if (value.Reference is not UsInstance instance)
            {
                throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, "Kayıt model değil.", node.Span.Start, node.Span.Length);
            }

            instance.EnsureActive();
            var store = query.Database.Open(query.Table);
            if (name == "ekle")
            {
                await Insert(store, instance, node.Span).ConfigureAwait(false);
            }
            else if (name == "güncelle")
            {
                await Update(store, instance, node.Span).ConfigureAwait(false);
            }
            else
            {
                Delete(store, instance, node.Span);
            }

            query.Database.LastPlan = name;
            return Done(UsValue.Unit);
        }

        throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"'{name}' veritabanında çalışmıyor.", node.Span.Start, node.Span.Length);
    }

    private async ValueTask<UsCollection> Materialize(UsTableQuery query, TextSpan span)
    {
        var chain = Chain(query);
        var store = query.Database.Open(query.Table);
        var rows = store.Rows.ToList();
        BoundNode? sort = null;
        var descending = false;
        var includes = new List<FieldSymbol>();
        foreach (var step in chain)
        {
            if (step.Operation == "filtrele" && step.Argument is not null)
            {
                var kept = new List<MemoryRow>();
                foreach (var row in rows)
                {
                    if (await Matches(step.Argument, row, query.Table).ConfigureAwait(false))
                    {
                        kept.Add(row);
                    }
                }

                rows = kept;
            }
            else if (step.Operation is "sırala" or "tsırala" && step.Argument is not null)
            {
                sort = step.Argument;
                descending = step.Descending;
            }
            else if (step.Operation == "birlikte" && step.Include is not null)
            {
                includes.Add(step.Include);
            }
        }

        if (sort is not null)
        {
            var keyed = new List<(MemoryRow Row, UsValue Key)>();
            foreach (var row in rows)
            {
                keyed.Add((row, await Select(sort, row, query.Table).ConfigureAwait(false)));
            }

            rows = (descending
                ? keyed.OrderByDescending(item => item.Key, UsValueComparer.Instance)
                : keyed.OrderBy(item => item.Key, UsValueComparer.Instance)).Select(item => item.Row).ToList();
        }

        var list = new UsCollection("liste", query.Table.Model);
        foreach (var row in rows)
        {
            var instance = MaterializeRow(row, query.Table);
            foreach (var include in includes)
            {
                Include(query.Database, instance, row, include);
            }

            list.Items.Add(UsValue.FromReference(instance));
        }

        return list;
    }

    private async ValueTask Insert(TableStore store, UsInstance instance, TextSpan span)
    {
        var key = KeyOf(store.Table);
        if (key is not null)
        {
            instance.Write(key, UsValue.FromInt(NextKey(store, key)), instance.RuntimeType, span.Start, span.Length);
        }

        await EnsureValid(instance, span).ConfigureAwait(false);
        EnsureUnique(store, instance, -1, span);
        store.Rows.Add(Capture(instance, store.Table));
    }

    private async ValueTask Update(TableStore store, UsInstance instance, TextSpan span)
    {
        var index = FindRow(store, instance, span);
        await EnsureValid(instance, span).ConfigureAwait(false);
        EnsureUnique(store, instance, index, span);
        store.Rows[index] = Capture(instance, store.Table);
    }

    private void Delete(TableStore store, UsInstance instance, TextSpan span)
    {
        var index = FindRow(store, instance, span);
        store.Rows.RemoveAt(index);
    }

    private int FindRow(TableStore store, UsInstance instance, TextSpan span)
    {
        var key = KeyOf(store.Table) ?? throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", span.Start, span.Length);
        if (!instance.Assigned(key))
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", span.Start, span.Length);
        }

        var value = instance.Read(key, span.Start, span.Length);
        var index = store.Rows.FindIndex(row => row.Values.TryGetValue(key, out var stored) && stored.Equals(value));
        if (index < 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", span.Start, span.Length);
        }

        return index;
    }

    private async ValueTask EnsureValid(UsInstance instance, TextSpan span)
    {
        foreach (var field in UsInstance.Fields(instance.Definition))
        {
            if (ModelOf(field.Type) is not null)
            {
                continue;
            }

            var value = instance.Assigned(field) ? instance.Read(field, span.Start, span.Length) : UsValue.Null;
            if (field.IsRequired && IsEmpty(value))
            {
                throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, $"'{field.Name}' zorunludur.", span.Start, span.Length);
            }

            if (field.MaxLength is not null && value.Kind == UsValueKind.Text)
            {
                var limit = await Evaluate(field.MaxLength).ConfigureAwait(false);
                if ((value.Text ?? string.Empty).Length > limit.Int)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, $"'{field.Name}' en çok {limit.Int} karakter olabilir.", span.Start, span.Length);
                }
            }

            if (field.RangeMinimum is not null && field.RangeMaximum is not null && !value.IsNull && value.Kind != UsValueKind.Text)
            {
                var minimum = await Evaluate(field.RangeMinimum).ConfigureAwait(false);
                var maximum = await Evaluate(field.RangeMaximum).ConfigureAwait(false);
                if (Number(value) < Number(minimum) || Number(value) > Number(maximum))
                {
                    throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, $"'{field.Name}' aralık dışındadır.", span.Start, span.Length);
                }
            }
        }
    }

    private static void EnsureUnique(TableStore store, UsInstance instance, int skip, TextSpan span)
    {
        foreach (var field in UsInstance.Fields(instance.Definition))
        {
            if (!field.IsUnique || !instance.Assigned(field))
            {
                continue;
            }

            var value = instance.Read(field, span.Start, span.Length);
            for (var i = 0; i < store.Rows.Count; i++)
            {
                if (i == skip)
                {
                    continue;
                }

                if (store.Rows[i].Values.TryGetValue(field, out var stored) && stored.Equals(value))
                {
                    throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, $"'{field.Name}' benzersiz olmalıdır.", span.Start, span.Length);
                }
            }
        }
    }

    private static MemoryRow Capture(UsInstance instance, TableSymbol table)
    {
        var row = new MemoryRow();
        foreach (var field in UsInstance.Fields(Definition(table)))
        {
            if (!instance.Assigned(field))
            {
                continue;
            }

            var value = instance.Read(field, 0, 0);
            if (ModelOf(field.Type) is not null && value.Reference is UsInstance related)
            {
                var key = KeyOf(related.Definition);
                if (key is not null && related.Assigned(key))
                {
                    var stored = related.Read(key, 0, 0);
                    if (stored.Kind == UsValueKind.Int)
                    {
                        row.Relations[field] = stored.Int;
                    }
                }

                continue;
            }

            row.Values[field] = value;
        }

        return row;
    }

    private static UsInstance MaterializeRow(MemoryRow row, TableSymbol table)
    {
        var instance = UsInstance.Allocate(table.Model);
        foreach (var field in UsInstance.Fields(Definition(table)))
        {
            if (row.Values.TryGetValue(field, out var value))
            {
                instance.Write(field, value, table.Model, 0, 0);
            }
        }

        return instance;
    }

    private static void Include(MemoryDatabase database, UsInstance instance, MemoryRow row, FieldSymbol field)
    {
        if (!row.Relations.TryGetValue(field, out var key))
        {
            return;
        }

        var model = ModelOf(field.Type);
        if (model is null)
        {
            return;
        }

        var related = database.OpenTables().FirstOrDefault(store => Definition(store.Table) == model);
        if (related is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{field.Name}' için tablo yok.", 0, 0);
        }

        var keyField = KeyOf(model);
        var match = keyField is null ? null : related.Rows.FirstOrDefault(item => item.Values.TryGetValue(keyField, out var value) && value.Kind == UsValueKind.Int && value.Int == key);
        if (match is null)
        {
            return;
        }

        instance.Write(field, UsValue.FromReference(MaterializeRow(match, related.Table)), instance.RuntimeType, 0, 0);
    }

    private async ValueTask<bool> Matches(BoundNode predicate, MemoryRow row, TableSymbol table)
    {
        var instance = MaterializeRow(row, table);
        _state.Items.Push(UsValue.FromReference(instance));
        try
        {
            return await IsTrue(predicate).ConfigureAwait(false);
        }
        finally
        {
            _state.Items.Pop();
        }
    }

    private async ValueTask<UsValue> Select(BoundNode selector, MemoryRow row, TableSymbol table)
    {
        var instance = MaterializeRow(row, table);
        _state.Items.Push(UsValue.FromReference(instance));
        try
        {
            return await Evaluate(selector).ConfigureAwait(false);
        }
        finally
        {
            _state.Items.Pop();
        }
    }

    private static int NextKey(TableStore store, FieldSymbol key)
    {
        var max = 0;
        foreach (var row in store.Rows)
        {
            if (row.Values.TryGetValue(key, out var value) && value.Kind == UsValueKind.Int && value.Int > max)
            {
                max = value.Int;
            }
        }

        return max + 1;
    }

    private static FieldSymbol? KeyOf(TableSymbol table) => KeyOf(Definition(table));

    private static FieldSymbol? KeyOf(SourceTypeSymbol model) =>
        UsInstance.Fields(model).FirstOrDefault(field => field.IsKey);

    private static SourceTypeSymbol Definition(TableSymbol table) =>
        table.Model is SourceTypeSymbol source ? source.Definition : throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Tablo modeli yok.", 0, 0);

    private static SourceTypeSymbol? ModelOf(TypeSymbol type)
    {
        var bare = type.WithNullable(false);
        return bare is SourceTypeSymbol source && source.Definition.TypeKind == SourceTypeKind.Model ? source.Definition : null;
    }

    private static List<UsTableQuery> Chain(UsTableQuery query)
    {
        var chain = new List<UsTableQuery>();
        for (var current = query; current is not null; current = current.Previous)
        {
            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }

    private static string Plan(UsTableQuery query, string tail)
    {
        var names = Chain(query).Select(step => step.Operation).Where(step => step is not null).Cast<string>().ToList();
        names.Add(tail);
        return string.Join(' ', names);
    }

    private static UsValue Done(UsValue value) => UsValue.FromReference(new UsOperation(Task.FromResult(value)));
}

sealed class UsValueComparer : IComparer<UsValue>
{
    public static readonly UsValueComparer Instance = new();

    public int Compare(UsValue left, UsValue right)
    {
        if (left.IsNull && right.IsNull)
        {
            return 0;
        }

        if (left.IsNull)
        {
            return -1;
        }

        if (right.IsNull)
        {
            return 1;
        }

        if (left.Kind == UsValueKind.Text && right.Kind == UsValueKind.Text)
        {
            return string.CompareOrdinal(left.Text, right.Text);
        }

        if (left.Kind == UsValueKind.Bool && right.Kind == UsValueKind.Bool)
        {
            return left.Bool.CompareTo(right.Bool);
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return ToDecimal(left).CompareTo(ToDecimal(right));
        }

        return string.CompareOrdinal(left.ToDisplay(), right.ToDisplay());
    }

    private static bool IsNumber(UsValue value) => value.Kind is UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal;

    private static decimal ToDecimal(UsValue value) => value.Kind switch
    {
        UsValueKind.Int => value.Int,
        UsValueKind.UInt => value.UInt,
        UsValueKind.Decimal => value.Decimal,
        UsValueKind.Float32 => (decimal)value.Float32,
        UsValueKind.Float64 => (decimal)value.Float64,
        _ => 0,
    };
}

enum TxState
{
    None,
    Active,
    Closed,
}
