using System;
using System.Collections.Generic;
using System.IO;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.World;

/// <summary>A circular territory claim of a military building (castle, tower), docs/06-economy.md §5.</summary>
public readonly record struct TerritoryClaim(int Id, byte Owner, int X, int Y, int Radius);

/// <summary>
/// Tile ownership from territory claims (docs/06-economy.md §5). A claim covers the tiles with
/// dx² + dy² ≤ r²; on overlap the older claim (lower id — ids grow monotonically) keeps the tile.
/// Updates are incremental around the changed claim; the result always equals a full rebuild
/// (<see cref="Rebuild"/>), which save/load relies on.
/// </summary>
public sealed class Territory
{
    /// <summary>Owner value of an unclaimed tile.</summary>
    public const byte NoOwner = 0xFF;
    /// <summary>Largest claim radius (serialized as ushort; far above any building's radius).</summary>
    public const int MaxRadius = ushort.MaxValue;

    private readonly int _edge;
    private readonly byte[] _owner;
    /// <summary>Id of the claim owning each tile; 0 = none (ids start at 1).</summary>
    private readonly int[] _claimOf;
    /// <summary>Live claims in ascending id order (= age order).</summary>
    private readonly List<TerritoryClaim> _claims = new();
    private readonly int[] _tilesOwned = new int[256];

    public Territory(int edge)
    {
        _edge = edge;
        _owner = new byte[edge * edge];
        _claimOf = new int[edge * edge];
        System.Array.Fill(_owner, NoOwner);
        NextClaimId = 1;
    }

    public int Edge => _edge;
    public int NextClaimId { get; private set; }
    public IReadOnlyList<TerritoryClaim> Claims => _claims;

    public byte OwnerAt(int tile) => _owner[tile];

    public byte OwnerAt(int x, int y) => _owner[y * _edge + x];

    /// <summary>Id of the claim that owns the tile, 0 if unclaimed.</summary>
    public int ClaimAt(int tile) => _claimOf[tile];

    /// <summary>Number of tiles owned by a slot.</summary>
    public int TilesOwnedBy(byte owner) => _tilesOwned[owner];

    /// <summary>Adds a claim (newest, so it only takes unclaimed tiles) and returns its id.</summary>
    public int AddClaim(byte owner, int x, int y, int radius)
    {
        if (owner == NoOwner) throw new System.ArgumentOutOfRangeException(nameof(owner));
        if ((uint)x >= (uint)_edge || (uint)y >= (uint)_edge) throw new System.ArgumentOutOfRangeException(nameof(x));
        if (radius < 0 || radius > MaxRadius) throw new System.ArgumentOutOfRangeException(nameof(radius));
        var claim = new TerritoryClaim(NextClaimId++, owner, x, y, radius);
        _claims.Add(claim);
        Apply(claim);
        return claim.Id;
    }

    /// <summary>Removes a claim; its tiles go to the oldest remaining claim covering them, else become unclaimed.</summary>
    public bool RemoveClaim(int id)
    {
        int index = IndexOf(id);
        if (index < 0) return false;
        var removed = _claims[index];
        _claims.RemoveAt(index);

        // Only claims whose disc intersects the removed claim's bounding box can inherit tiles (kept in age order).
        var candidates = new List<TerritoryClaim>();
        foreach (var c in _claims)
            if (System.Math.Abs(c.X - removed.X) <= c.Radius + removed.Radius
                && System.Math.Abs(c.Y - removed.Y) <= c.Radius + removed.Radius)
                candidates.Add(c);

        Bounds(removed, out int x0, out int y0, out int x1, out int y1);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int tile = y * _edge + x;
                if (_claimOf[tile] != id) continue;
                Set(tile, 0, NoOwner);
                foreach (var c in candidates)
                {
                    if (!Covers(c, x, y)) continue;
                    Set(tile, c.Id, c.Owner);
                    break;
                }
            }
        }
        return true;
    }

    /// <summary>Recomputes every tile from the claim list (load path; reference for the incremental updates).</summary>
    public void Rebuild()
    {
        System.Array.Fill(_owner, NoOwner);
        System.Array.Clear(_claimOf);
        System.Array.Clear(_tilesOwned);
        foreach (var c in _claims) Apply(c);
    }

    /// <summary>Canonical state: next id, claims, then the owner grid (hashed so incremental bugs surface as desyncs).</summary>
    public void WriteTo(CanonicalWriter w)
    {
        w.WriteInt32(NextClaimId);
        w.WriteInt32(_claims.Count);
        foreach (var c in _claims)
        {
            w.WriteInt32(c.Id);
            w.WriteByte(c.Owner);
            w.WriteUInt16((ushort)c.X);
            w.WriteUInt16((ushort)c.Y);
            w.WriteUInt16((ushort)c.Radius);
        }
        w.WriteRaw(_owner);
    }

    public static Territory ReadFrom(CanonicalReader r, int edge)
    {
        var t = new Territory(edge);
        t.NextClaimId = r.ReadInt32();
        if (t.NextClaimId < 1) throw new InvalidDataException("Invalid next claim id");
        int count = r.ReadInt32();
        if (count < 0 || count > edge * edge) throw new InvalidDataException("Invalid claim count");
        int lastId = 0;
        for (int i = 0; i < count; i++)
        {
            var c = new TerritoryClaim(r.ReadInt32(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16());
            if (c.Id <= lastId || c.Id >= t.NextClaimId || c.Owner == NoOwner || c.X >= edge || c.Y >= edge)
                throw new InvalidDataException("Invalid territory claim");
            lastId = c.Id;
            t._claims.Add(c);
        }
        t.Rebuild();
        var saved = r.ReadRaw(edge * edge);
        if (!saved.AsSpan().SequenceEqual(t._owner)) throw new InvalidDataException("Territory grid does not match its claims");
        return t;
    }

    private void Apply(in TerritoryClaim c)
    {
        Bounds(c, out int x0, out int y0, out int x1, out int y1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int tile = y * _edge + x;
                if (_claimOf[tile] == 0 && Covers(c, x, y)) Set(tile, c.Id, c.Owner);
            }
    }

    private void Set(int tile, int claim, byte owner)
    {
        if (_owner[tile] != NoOwner) _tilesOwned[_owner[tile]]--;
        _claimOf[tile] = claim;
        _owner[tile] = owner;
        if (owner != NoOwner) _tilesOwned[owner]++;
    }

    private static bool Covers(in TerritoryClaim c, int x, int y)
    {
        long dx = x - c.X, dy = y - c.Y;
        return dx * dx + dy * dy <= (long)c.Radius * c.Radius;
    }

    private void Bounds(in TerritoryClaim c, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = System.Math.Max(0, c.X - c.Radius);
        y0 = System.Math.Max(0, c.Y - c.Radius);
        x1 = System.Math.Min(_edge - 1, c.X + c.Radius);
        y1 = System.Math.Min(_edge - 1, c.Y + c.Radius);
    }

    private int IndexOf(int id)
    {
        int lo = 0, hi = _claims.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cur = _claims[mid].Id;
            if (cur == id) return mid;
            if (cur < id) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }
}
