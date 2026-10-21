namespace Ghanapoliticaldirectory.Domain
{
    /// <summary>What a signed-in user may do. The server checks these on every sensitive action.</summary>
    public static class Permissions
    {
        public const string AdminAccess = "admin.access";
        public const string RecordsEnter = "records.enter";
        public const string RecordsVerify = "records.verify";
        public const string RecordsApprove = "records.approve";
        public const string RecordsPublish = "records.publish";
        public const string RecordsCorrect = "records.correct";
        public const string RecordsArchive = "records.archive";
        public const string ImportRun = "import.run";
        public const string AuditView = "audit.view";
        public const string UsersManage = "users.manage";
        public const string SettingsManage = "settings.manage";

        public static readonly IReadOnlyList<string> All = new[]
        {
        AdminAccess, RecordsEnter, RecordsVerify, RecordsApprove, RecordsPublish, RecordsCorrect,
        RecordsArchive, ImportRun, AuditView, UsersManage, SettingsManage
    };
    }


    public static class Roles
    {
        public const string DataEntry = "DataEntry";
        public const string Verifier = "Verifier";
        public const string Approver = "Approver";
        public const string Publisher = "Publisher";
        public const string Administrator = "Administrator";

        public static readonly IReadOnlyList<string> All = new[] { DataEntry, Verifier, Approver, Publisher, Administrator };
    }
    public class Security
    {
    }


    public static class RolePermissions
    {
        private static readonly Dictionary<string, string[]> Map = new()
        {
            [Roles.DataEntry] = new[] { Permissions.AdminAccess, Permissions.RecordsEnter, Permissions.ImportRun },
            [Roles.Verifier] = new[] { Permissions.AdminAccess, Permissions.RecordsVerify, Permissions.AuditView },
            [Roles.Approver] = new[] { Permissions.AdminAccess, Permissions.RecordsApprove, Permissions.AuditView },
            [Roles.Publisher] = new[] { Permissions.AdminAccess, Permissions.RecordsPublish, Permissions.RecordsCorrect, Permissions.AuditView },
            [Roles.Administrator] = Permissions.All.ToArray()
        };

        public static IReadOnlySet<string> For(IEnumerable<string> roles)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in roles)
                if (Map.TryGetValue(role, out var perms))
                    foreach (var p in perms) set.Add(p);
            return set;
        }

        public static IReadOnlyCollection<string> PermissionsOf(string role) =>
            Map.TryGetValue(role, out var perms) ? perms : Array.Empty<string>();
    }

}
