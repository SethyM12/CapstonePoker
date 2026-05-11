using System.Collections.Concurrent;
using MilesHighPoker.Models;

namespace MilesHighPoker.Services;

public sealed class TableRegistry
{
    private ConcurrentDictionary<String, Table> tables = new();

    public Table GetOrCreateTable(String tableId)
    {
        if(String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));
        
        return tables.GetOrAdd(tableId, new Table(tableId));
    }

    public String CreateTable()
    {
        String tableId = Guid.NewGuid().ToString();
        tables.TryAdd(tableId, new Table(tableId));
        return tableId;
    }

    public bool TryGetTable(String tableId, out Table? table)
    {
        table = tables.GetValueOrDefault(tableId);
        return table != null;
    }

    public bool TableExists(String tableId)
    {
        return tables.ContainsKey(tableId);
    }
    
    public bool RemoveTable(String tableId)
    {
        return tables.TryRemove(tableId, out _);
    }
}