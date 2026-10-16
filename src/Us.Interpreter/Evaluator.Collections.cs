using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed class UsCollection
{
    public UsCollection(string kind, TypeSymbol element, TypeSymbol? key = null)
    {
        Kind = kind;
        Element = element;
        Key = key;
    }

    public string Kind { get; }

    public TypeSymbol Element { get; }

    public TypeSymbol? Key { get; }

    public List<UsValue> Items { get; } = [];

    public List<UsValue> Keys { get; } = [];

    public int Version { get; private set; }

    public int Count => Kind == "sözlük" ? Keys.Count : Items.Count;

    public void Touch() => Version++;

    public UsCollection Copy()
    {
        var copy = new UsCollection(Kind, Element, Key);
        copy.Items.AddRange(Items);
        copy.Keys.AddRange(Keys);
        return copy;
    }
}

public sealed partial class Evaluator
{
    private async ValueTask<Signal> ForEach(BoundNode node)
    {
        var collection = RequireCollection(await Evaluate(node.Children[0]), node.Children[0].Span);
        if (collection.Kind is "küme" or "sözlük")
        {
            throw new UsRuntimeException(UsRuntimeCodes.QueryTarget, $"'{collection.Kind}' dön ile gezilmez.", node.Span.Start, node.Span.Length);
        }

        var version = collection.Version;
        _state.Frames.Push(new Dictionary<Symbol, Cell>());
        try
        {
            for (var i = 0; i < collection.Items.Count; i++)
            {
                if (collection.Version != version)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.CollectionModified, "Gezilen koleksiyon değişti.", node.Span.Start, node.Span.Length);
                }

                var cell = new Cell(node.Symbol is VariableSymbol variable && variable.Type.IsNullable);
                _state.Frames.Peek()[node.Symbol!] = cell;
                cell.Write(collection.Items[i], node.Span.Start, node.Span.Length);
                var signal = await Execute(node.Children[1]);
                if (signal.Kind == SignalKind.Break)
                {
                    break;
                }

                if (signal.Kind == SignalKind.Return)
                {
                    return signal;
                }
            }
        }
        finally
        {
            _state.Frames.Pop();
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<UsValue> CollectionLiteral(BoundNode node)
    {
        var kind = CollectionKind(node.Type);
        var element = ElementOf(node.Type);
        var key = node.Type is ConstructedTypeSymbol constructed && constructed.Arguments.Length == 2 ? constructed.Arguments[0] : null;
        var collection = new UsCollection(kind, element ?? node.Type, key);
        foreach (var child in node.Children)
        {
            Add(collection, await Evaluate(child), child.Span, fromLiteral: true);
        }

        return UsValue.FromReference(collection);
    }

    private async ValueTask<UsValue> Index(BoundNode node)
    {
        var receiver = await Evaluate(node.Children[0]);
        var index = RequireIndex(await Evaluate(node.Children[1]), node.Children[1].Span);
        if (receiver.Kind == UsValueKind.Text)
        {
            var text = receiver.Text ?? string.Empty;
            if (index < 0 || index >= text.Length)
            {
                throw new UsRuntimeException(UsRuntimeCodes.IndexRange, "İndeks aralık dışında.", node.Span.Start, node.Span.Length);
            }

            return UsValue.FromChar(text[index]);
        }

        var collection = RequireCollection(receiver, node.Span);
        EnsureIndexed(collection, node.Span);
        if (index < 0 || index >= collection.Items.Count)
        {
            throw new UsRuntimeException(UsRuntimeCodes.IndexRange, "İndeks aralık dışında.", node.Span.Start, node.Span.Length);
        }

        return collection.Items[index];
    }

    private async ValueTask AssignIndex(BoundNode node, UsValue value)
    {
        var collection = RequireCollection(await Evaluate(node.Children[0]), node.Span);
        EnsureIndexed(collection, node.Span);
        var index = RequireIndex(await Evaluate(node.Children[1]), node.Children[1].Span);
        if (index < 0 || index >= collection.Items.Count)
        {
            throw new UsRuntimeException(UsRuntimeCodes.IndexRange, "İndeks aralık dışında.", node.Span.Start, node.Span.Length);
        }

        collection.Items[index] = value;
        collection.Touch();
    }

    private async ValueTask<UsValue> CollectionProperty(string name, UsValue receiver, TextSpan span)
    {
        var collection = RequireCollection(receiver, span);
        switch (name)
        {
            case "adet" or "uzunluk":
                return UsValue.FromInt(collection.Count);
            case "ilk":
                return await First(collection, null, span, orNull: false).ConfigureAwait(false);
            case "min":
                return await Extreme(collection, null, span, max: false).ConfigureAwait(false);
            case "maks":
                return await Extreme(collection, null, span, max: true).ConfigureAwait(false);
            case "toplam":
                return await Sum(collection, null, span).ConfigureAwait(false);
            case "ortalama":
                return await Average(collection, null, span).ConfigureAwait(false);
            default:
                throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Koleksiyon üyesi yok.", span.Start, span.Length);
        }
    }

    private async ValueTask<UsValue> Query(string name, BoundNode node)
    {
        var collection = RequireCollection(await Evaluate(node.Children[0]), node.Children[0].Span);
        var argument = node.Children.Length > 1 ? node.Children[1] : null;
        var version = collection.Version;
        switch (name)
        {
            case "adet":
                return argument is null ? UsValue.FromInt(collection.Count) : UsValue.FromInt(await CountWhere(collection, argument, version).ConfigureAwait(false));
            case "ekle":
                return await AddCall(collection, node).ConfigureAwait(false);
            case "kaldır":
                return UsValue.FromBool(Remove(collection, await Evaluate(node.Children[1]).ConfigureAwait(false), node.Span));
            case "içerir":
                return UsValue.FromBool(Contains(collection, await Evaluate(node.Children[1]).ConfigureAwait(false)));
            case "filtrele":
                return UsValue.FromReference(await Filter(collection, argument!, version).ConfigureAwait(false));
            case "sırala":
                return UsValue.FromReference(await Sort(collection, argument, ascending: true).ConfigureAwait(false));
            case "tsırala":
                return UsValue.FromReference(await Sort(collection, argument, ascending: false).ConfigureAwait(false));
            case "tersÇevir":
                return UsValue.FromReference(Reverse(collection));
            case "ilk":
                return await First(collection, argument, node.Span, orNull: false).ConfigureAwait(false);
            case "ilkYadaBoş":
            case "bul":
                return await First(collection, argument, node.Span, orNull: true).ConfigureAwait(false);
            case "varMı":
                return UsValue.FromBool(await Any(collection, argument!, version).ConfigureAwait(false));
            case "tümü":
                return UsValue.FromBool(await All(collection, argument!, version).ConfigureAwait(false));
            case "toplam":
                return await Sum(collection, argument, node.Span).ConfigureAwait(false);
            case "ortalama":
                return await Average(collection, argument, node.Span).ConfigureAwait(false);
            case "min":
                return await Extreme(collection, argument, node.Span, max: false).ConfigureAwait(false);
            case "maks":
                return await Extreme(collection, argument, node.Span, max: true).ConfigureAwait(false);
            case "benzersiz":
                return UsValue.FromReference(await Distinct(collection).ConfigureAwait(false));
            case "atla":
                return UsValue.FromReference(await Window(collection, argument!, skip: true, node.Span).ConfigureAwait(false));
            case "sınırla":
                return UsValue.FromReference(await Window(collection, argument!, skip: false, node.Span).ConfigureAwait(false));
            default:
                throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Bu koleksiyon üyesi çalışmıyor.", node.Span.Start, node.Span.Length);
        }
    }

    private async ValueTask<UsValue> ConvertWith(MappingSymbol mapping, BoundNode node)
    {
        var source = await Evaluate(node.Children[0]);
        var instance = UsInstance.Allocate(mapping.Destination);
        await ApplyDefaults(instance, instance.Definition, node.Span);
        _state.Items.Push(source);
        try
        {
            foreach (var (field, value) in mapping.Assignments)
            {
                WriteField(UsValue.FromReference(instance), field, await Evaluate(value), value.Span);
            }
        }
        finally
        {
            _state.Items.Pop();
        }

        if (instance.Definition.TypeKind == SourceTypeKind.Model)
        {
            foreach (var field in instance.Definition.Fields)
            {
                if (instance.Assigned(field))
                {
                    continue;
                }

                if (field.IsRequired || !field.Type.IsNullable && field.Default is null)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.MissingField, $"'{field.Name}' alanı verilmelidir.", node.Span.Start, node.Span.Length);
                }
            }
        }

        return UsValue.FromReference(instance);
    }

    private async ValueTask<UsValue> AddCall(UsCollection collection, BoundNode node)
    {
        if (collection.Kind == "dizi")
        {
            throw new UsRuntimeException(UsRuntimeCodes.FixedCollection, "diziye eklenemez.", node.Span.Start, node.Span.Length);
        }

        if (collection.Kind == "sözlük")
        {
            var key = await Evaluate(node.Children[1]);
            var value = await Evaluate(node.Children[2]);
            if (ContainsKey(collection, key))
            {
                throw new UsRuntimeException(UsRuntimeCodes.DuplicateKey, "sözlük anahtarı yinelenemez.", node.Span.Start, node.Span.Length);
            }

            collection.Keys.Add(key);
            collection.Items.Add(value);
            collection.Touch();
            return UsValue.FromReference(collection);
        }

        Add(collection, await Evaluate(node.Children[1]), node.Children[1].Span, fromLiteral: false);
        return UsValue.FromReference(collection);
    }

    private void Add(UsCollection collection, UsValue value, TextSpan span, bool fromLiteral)
    {
        if (collection.Kind == "küme" && Contains(collection, value))
        {
            return;
        }

        if (collection.Kind == "dizi" && !fromLiteral)
        {
            throw new UsRuntimeException(UsRuntimeCodes.FixedCollection, "diziye eklenemez.", span.Start, span.Length);
        }

        collection.Items.Add(value);
        if (!fromLiteral)
        {
            collection.Touch();
        }
    }

    private bool Remove(UsCollection collection, UsValue value, TextSpan span)
    {
        if (collection.Kind == "dizi")
        {
            throw new UsRuntimeException(UsRuntimeCodes.FixedCollection, "diziden kaldırılamaz.", span.Start, span.Length);
        }

        for (var i = 0; i < collection.Items.Count; i++)
        {
            if (SameValue(collection.Items[i], value))
            {
                collection.Items.RemoveAt(i);
                if (collection.Kind == "sözlük" && i < collection.Keys.Count)
                {
                    collection.Keys.RemoveAt(i);
                }

                collection.Touch();
                return true;
            }
        }

        return false;
    }

    private bool Contains(UsCollection collection, UsValue value)
    {
        foreach (var item in collection.Items)
        {
            if (SameValue(item, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsKey(UsCollection collection, UsValue key)
    {
        foreach (var item in collection.Keys)
        {
            if (SameValue(item, key))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<UsCollection> Filter(UsCollection source, BoundNode predicate, int version)
    {
        var result = new UsCollection(source.Kind, source.Element, source.Key);
        foreach (var item in source.Items)
        {
            Guard(source, version, predicate.Span);
            if (await IsMatch(predicate, item))
            {
                result.Items.Add(item);
            }
        }

        return result;
    }

    private async ValueTask<int> CountWhere(UsCollection source, BoundNode predicate, int version)
    {
        var count = 0;
        foreach (var item in source.Items)
        {
            Guard(source, version, predicate.Span);
            if (await IsMatch(predicate, item))
            {
                count++;
            }
        }

        return count;
    }

    private async ValueTask<bool> Any(UsCollection source, BoundNode predicate, int version)
    {
        foreach (var item in source.Items)
        {
            Guard(source, version, predicate.Span);
            if (await IsMatch(predicate, item))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<bool> All(UsCollection source, BoundNode predicate, int version)
    {
        foreach (var item in source.Items)
        {
            Guard(source, version, predicate.Span);
            if (!await IsMatch(predicate, item))
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask<UsValue> First(UsCollection source, BoundNode? predicate, TextSpan span, bool orNull)
    {
        foreach (var item in source.Items)
        {
            if (predicate is null || await IsMatch(predicate, item))
            {
                return item;
            }
        }

        if (orNull)
        {
            return UsValue.Null;
        }

        throw new UsRuntimeException(UsRuntimeCodes.EmptyFirst, "ilk öğe yok.", span.Start, span.Length);
    }

    private async ValueTask<UsCollection> Sort(UsCollection source, BoundNode? selector, bool ascending)
    {
        var keyed = new List<(UsValue Item, UsValue Key, int Index)>(source.Items.Count);
        for (var i = 0; i < source.Items.Count; i++)
        {
            var item = source.Items[i];
            keyed.Add((item, selector is null ? item : await Apply(selector, item), i));
        }

        keyed.Sort((left, right) =>
        {
            var compared = Compare(left.Key, right.Key, selector?.Span ?? default);
            if (compared == 0)
            {
                return left.Index.CompareTo(right.Index);
            }

            return ascending ? compared : -compared;
        });
        var result = new UsCollection(source.Kind, source.Element, source.Key);
        foreach (var entry in keyed)
        {
            result.Items.Add(entry.Item);
        }

        return result;
    }

    private static UsCollection Reverse(UsCollection source)
    {
        var result = source.Copy();
        result.Items.Reverse();
        result.Keys.Reverse();
        return result;
    }

    private async ValueTask<UsCollection> Distinct(UsCollection source)
    {
        var result = new UsCollection(source.Kind, source.Element, source.Key);
        foreach (var item in source.Items)
        {
            if (!Contains(result, item))
            {
                result.Items.Add(item);
            }
        }

        return result;
    }

    private async ValueTask<UsCollection> Window(UsCollection source, BoundNode countNode, bool skip, TextSpan span)
    {
        var count = RequireIndex(await Evaluate(countNode), countNode.Span);
        if (count < 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NegativeWindow, "Pencere negatif olamaz.", span.Start, span.Length);
        }

        var result = new UsCollection(source.Kind, source.Element, source.Key);
        if (skip)
        {
            for (var i = count; i < source.Items.Count; i++)
            {
                result.Items.Add(source.Items[i]);
            }
        }
        else
        {
            var end = Math.Min(count, source.Items.Count);
            for (var i = 0; i < end; i++)
            {
                result.Items.Add(source.Items[i]);
            }
        }

        return result;
    }

    private async ValueTask<UsValue> Sum(UsCollection source, BoundNode? selector, TextSpan span) =>
        UsValue.FromFloat64(await Total(source, selector, span));

    private async ValueTask<double> Total(UsCollection source, BoundNode? selector, TextSpan span)
    {
        double total = 0;
        foreach (var item in source.Items)
        {
            total += Number(selector is null ? item : await Apply(selector, item), span);
        }

        return total;
    }

    private async ValueTask<UsValue> Average(UsCollection source, BoundNode? selector, TextSpan span)
    {
        if (source.Items.Count == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.EmptyExtreme, "Boş koleksiyonun ortalaması yok.", span.Start, span.Length);
        }

        return UsValue.FromFloat64(await Total(source, selector, span) / source.Items.Count);
    }

    private async ValueTask<UsValue> Extreme(UsCollection source, BoundNode? selector, TextSpan span, bool max)
    {
        if (source.Items.Count == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.EmptyExtreme, max ? "Boş koleksiyonun maks değeri yok." : "Boş koleksiyonun min değeri yok.", span.Start, span.Length);
        }

        var bestItem = source.Items[0];
        var bestKey = selector is null ? bestItem : await Apply(selector, bestItem);
        for (var i = 1; i < source.Items.Count; i++)
        {
            var item = source.Items[i];
            var key = selector is null ? item : await Apply(selector, item);
            var compared = Compare(key, bestKey, span);
            if (max ? compared > 0 : compared < 0)
            {
                bestItem = item;
                bestKey = key;
            }
        }

        return bestItem;
    }

    private async ValueTask<bool> IsMatch(BoundNode predicate, UsValue item)
    {
        var value = await Apply(predicate, item);
        return value.Kind == UsValueKind.Bool && value.Bool;
    }

    private async ValueTask<UsValue> Apply(BoundNode node, UsValue item)
    {
        _state.Items.Push(item);
        try
        {
            if (node.Symbol is FunctionSymbol { IsLambda: true } lambda && lambda.Parameters.Count == 1)
            {
                return await CallItem(lambda, item, node.Span);
            }

            return await Evaluate(node);
        }
        finally
        {
            _state.Items.Pop();
        }
    }

    private async ValueTask<UsValue> CallItem(FunctionSymbol lambda, UsValue item, TextSpan span)
    {
        var frame = new Dictionary<Symbol, Cell>();
        var parameter = lambda.Parameters[0];
        var cell = new Cell(parameter.Type.IsNullable);
        cell.Write(item, span.Start, span.Length);
        frame[parameter] = cell;
        _state.Frames.Push(frame);
        _state.Functions.Push(lambda);
        try
        {
            var signal = await Execute(lambda.BoundBody ?? throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Lambda gövdesi yok.", span.Start, span.Length));
            return signal.Kind == SignalKind.Return ? signal.Value : UsValue.Unit;
        }
        finally
        {
            _state.Functions.Pop();
            _state.Frames.Pop();
        }
    }

    private static void Guard(UsCollection collection, int version, TextSpan span)
    {
        if (collection.Version != version)
        {
            throw new UsRuntimeException(UsRuntimeCodes.CollectionModified, "Sorgunun kaynağı değişti.", span.Start, span.Length);
        }
    }

    private static int Compare(UsValue left, UsValue right, TextSpan span)
    {
        if (left.Kind == UsValueKind.Text && right.Kind == UsValueKind.Text)
        {
            return string.Compare(left.Text, right.Text, StringComparison.Ordinal);
        }

        if (left.Kind == UsValueKind.Char && right.Kind == UsValueKind.Char)
        {
            return left.Char.CompareTo(right.Char);
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return Number(left, span).CompareTo(Number(right, span));
        }

        throw new UsRuntimeException(UsRuntimeCodes.QuerySelector, "Bu değer sıralanamaz.", span.Start, span.Length);
    }

    private static bool IsNumber(UsValue value) => value.Kind is UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal;

    private static double Number(UsValue value, TextSpan span) => value.Kind switch
    {
        UsValueKind.Int => value.Int,
        UsValueKind.UInt => value.UInt,
        UsValueKind.Float64 => value.Float64,
        UsValueKind.Float32 => value.Float32,
        UsValueKind.Decimal => (double)value.Decimal,
        _ => throw new UsRuntimeException(UsRuntimeCodes.QuerySelector, "Sayısal değer gerekir.", span.Start, span.Length),
    };

    private static bool SameValue(UsValue left, UsValue right)
    {
        if (left.Reference is UsInstance leftInstance && right.Reference is UsInstance rightInstance)
        {
            if (leftInstance.Definition.TypeKind == SourceTypeKind.Model && rightInstance.Definition.TypeKind == SourceTypeKind.Model && ReferenceEquals(leftInstance.Definition, rightInstance.Definition))
            {
                foreach (var field in UsInstance.Fields(leftInstance.Definition))
                {
                    var leftAssigned = leftInstance.Assigned(field);
                    var rightAssigned = rightInstance.Assigned(field);
                    if (leftAssigned != rightAssigned)
                    {
                        return false;
                    }

                    if (leftAssigned && !SameValue(leftInstance.Read(field, 0, 0), rightInstance.Read(field, 0, 0)))
                    {
                        return false;
                    }
                }

                return true;
            }

            return ReferenceEquals(leftInstance, rightInstance);
        }

        if (left.Kind == UsValueKind.Object || right.Kind == UsValueKind.Object)
        {
            return ReferenceEquals(left.Reference, right.Reference);
        }

        return left.Kind == right.Kind && left.Equals(right);
    }

    private static int RequireIndex(UsValue value, TextSpan span)
    {
        if (value.Kind != UsValueKind.Int)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "İndeks tamsayı ister.", span.Start, span.Length);
        }

        return value.Int;
    }

    private static void EnsureIndexed(UsCollection collection, TextSpan span)
    {
        if (collection.Kind is not ("liste" or "dizi" or "sliste"))
        {
            throw new UsRuntimeException(UsRuntimeCodes.QueryTarget, $"'{collection.Kind}' indislenemez.", span.Start, span.Length);
        }
    }

    private static UsCollection RequireCollection(UsValue value, TextSpan span)
    {
        if (value.Reference is UsCollection collection)
        {
            return collection;
        }

        throw new UsRuntimeException(UsRuntimeCodes.QueryTarget, "Koleksiyon gerekir.", span.Start, span.Length);
    }

    private static string CollectionKind(TypeSymbol type)
    {
        var name = type is ConstructedTypeSymbol constructed ? constructed.Definition.Name : "liste";
        return name is "dizi" or "küme" or "sözlük" or "sliste" ? name : "liste";
    }

    private static TypeSymbol? ElementOf(TypeSymbol type) =>
        type is ConstructedTypeSymbol constructed && constructed.Arguments.Length > 0
            ? constructed.Arguments[^1]
            : null;
}
