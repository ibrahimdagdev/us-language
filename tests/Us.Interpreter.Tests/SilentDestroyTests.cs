using System.Text.Json;
using Us.Core;
using Us.Interpreter;

namespace Us.Interpreter.Tests;

public class SilentDestroyTests
{
    [Fact]
    public void DestructiveDiffsNeedApprovalAndHistoryOmitsRowData()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-m17-destroy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                    ad: metin
                    parola: metin
                }
                model Sipariş
                {
                    id: tamsayı : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                    tablo siparişler: Sipariş
                }
                """);
            Assert.True(SyncDatabase.Run(root, false).Applied);

            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);

            var dropBlocked = SyncDatabase.Run(root, false);
            Assert.False(dropBlocked.Applied);
            Assert.Equal(DiagnosticCodes.DestructiveSchema, Assert.Single(dropBlocked.Diagnostics).Code);
            Assert.Contains(dropBlocked.Steps, step => step.Kind == SchemaStepKind.DropColumn);
            Assert.Contains(dropBlocked.Steps, step => step.Kind == SchemaStepKind.DropTable);

            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: metin : anahtar
                    ad: metin
                    parola: metin
                }
                model Sipariş
                {
                    id: tamsayı : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                    tablo siparişler: Sipariş
                }
                """);
            var narrowBlocked = SyncDatabase.Run(root, false);
            Assert.False(narrowBlocked.Applied);
            Assert.Equal(DiagnosticCodes.DestructiveSchema, Assert.Single(narrowBlocked.Diagnostics).Code);
            Assert.Contains(narrowBlocked.Steps, step => step.Kind == SchemaStepKind.NarrowColumn);

            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                    ad: metin
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);
            var approved = SyncDatabase.Run(root, true);
            Assert.True(approved.Applied);
            Assert.Contains(approved.Steps, step => step.Kind == SchemaStepKind.DropColumn && step.Column == "parola");
            Assert.Contains(approved.Steps, step => step.Kind == SchemaStepKind.DropTable && step.Table == "siparişler");

            var document = JsonSerializer.Deserialize<SchemaDocument>(File.ReadAllText(SyncDatabase.FilePath(root)))!;
            var destructiveNotes = document.History
                .Where(item => item.Kind is nameof(SchemaStepKind.DropColumn) or nameof(SchemaStepKind.DropTable) or nameof(SchemaStepKind.NarrowColumn))
                .ToArray();
            Assert.NotEmpty(destructiveNotes);
            foreach (var record in destructiveNotes)
            {
                Assert.Equal("Geri alma notu: yıkıcı değişiklik onaylandı.", record.Note);
                Assert.DoesNotContain("satır", record.Note, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("değer", record.Note, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain('@', record.Note);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
