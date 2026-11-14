/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenSim.Continuum.WebUI
{
    public sealed class WebUINewsArticle
    {
        public Guid ID { get; set; }
        public Guid AuthorID { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public bool Published { get; set; }
        public int Revision { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>Provider-independent portal content. No grid service configuration or credentials.</summary>
    public sealed class WebUIContentStore
    {
        private readonly string m_path;
        private readonly object m_gate = new();
        public WebUIContentStore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("WebUI ContentStorePath is required");
            m_path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(m_path));
            Read(); // fail on corrupt/unreadable data; never silently reset content
        }
        public WebUINewsArticle[] Read()
        {
            lock (m_gate)
            {
                if (!File.Exists(m_path)) return Array.Empty<WebUINewsArticle>();
                using var stream = new FileStream(m_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("WebUI content exceeds its storage bound");
                var rows = JsonSerializer.Deserialize<List<WebUINewsArticle>>(stream)
                    ?? throw new InvalidDataException("Invalid WebUI content");
                if (rows.Count > 500 || rows.Any(x => x == null || x.ID == Guid.Empty || x.AuthorID == Guid.Empty
                    || x.Revision < 1 || String.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 160
                    || String.IsNullOrWhiteSpace(x.Text) || x.Text.Length > 8192
                    || x.CreatedUtc.Kind != DateTimeKind.Utc || x.UpdatedUtc.Kind != DateTimeKind.Utc)
                    || rows.Select(x => x.ID).Distinct().Count() != rows.Count)
                    throw new InvalidDataException("Invalid WebUI content records");
                return rows.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.ID).ToArray();
            }
        }
        public Guid Save(Guid id, int revision, Guid actor, string title, string text, bool published)
        {
            title = title?.Trim(); text = text?.Trim();
            if (actor == Guid.Empty || String.IsNullOrWhiteSpace(title) || title.Length > 160
                || String.IsNullOrWhiteSpace(text) || text.Length > 8192 || title.Any(Char.IsControl))
                throw new ArgumentException("Use a title of 1–160 characters and text of 1–8192 characters");
            Guid saved = id;
            Mutate(rows =>
            {
                var row = rows.FirstOrDefault(x => x.ID == id);
                if (id == Guid.Empty)
                {
                    if (revision != 0 || rows.Count >= 500) throw new InvalidOperationException("News limit reached or invalid new article revision");
                    row = new WebUINewsArticle { ID = Guid.NewGuid(), AuthorID = actor, CreatedUtc = DateTime.UtcNow };
                    rows.Add(row);
                }
                else if (row == null || row.Revision != revision)
                    throw new InvalidOperationException("This article changed or was deleted. Reload before saving");
                row.Title = title; row.Text = text; row.Published = published;
                row.Revision = checked(row.Revision + 1); row.UpdatedUtc = DateTime.UtcNow;
                saved = row.ID;
            });
            return saved;
        }
        public void Delete(Guid id, int revision)
        {
            Mutate(rows =>
            {
                var row = rows.FirstOrDefault(x => x.ID == id);
                if (row == null || row.Revision != revision)
                    throw new InvalidOperationException("This article changed or was deleted. Reload before deleting");
                rows.Remove(row);
            });
        }
        private void Mutate(Action<List<WebUINewsArticle>> update)
        {
            lock (m_gate)
            {
                // Shared path supports multiple processes: reload only after acquiring its lease.
                // A competing writer gets an explicit failure, never a lost update.
                using var lease = new FileStream(m_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var rows = Read().ToList(); update(rows);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(rows);
                if (bytes.Length > 8 * 1024 * 1024)
                    throw new InvalidOperationException("News storage limit reached. Remove old articles before saving");
                using (var stream = new FileStream(m_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(true); }
                File.Move(m_path + ".tmp", m_path, true);
            }
        }
    }
}
