using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sapling.Web.Data;

/// <summary>Seeds the shared catalogue plus the PRD demo student and one starter resume.</summary>
public static class DemoSeeder
{
    public const string DemoEmail = "demo@sapling.app";
    public const string DemoInstituteEmail = "institute@sapling.app";
    public const string DemoAdminEmail = "admin@sapling.app";
    public const string DemoPassword = "Sapling@2026";

    /// <summary>The demo institute owns this college, which is also the demo student's.</summary>
    private const string DemoCollege = "Shri Govindram Seksaria Institute of Technology and Science";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SaplingDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Ensure State column exists in StudentProfiles table on SQLite
        await EnsureSchemaColumnsAsync(db);

        // Must run before anything reads a student or a user, which the catalogue sync below does.
        await EnsureAccountSchemaAsync(db);
        await EnsureCareerSchemaAsync(db);

        // Must run before any profile insert: older DBs have NOT NULL ATS columns the model no longer sets.
        await EnsureResumeSchemaAsync(db);

        // Exams are a maintained catalogue with real dates, not demo data, so they sync on every start.
        await EnsureExamSchemaAsync(db);

        if (!await db.Skills.AnyAsync())
        {
            await SeedCatalogueAsync(db);
        }
        else
        {
            // Ensure any new catalogue skills are added to an existing DB.
            await EnsureSkillCatalogueAsync(db);
        }

        // Roles must exist before the demo student, who is aimed at one of them.
        var catalogue = scope.ServiceProvider.GetRequiredService<Services.CareerCatalogueFile>();
        await catalogue.SyncAsync(db, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CareerCatalogue"));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(DemoEmail) is null)
        {
            await SeedDemoStudentAsync(db, users);
        }

        // Seed colleges directory from AICTE dataset if not already populated
        await EnsureCollegesSeededAsync(db);

        await EnsureCommunitySchemaAsync(db);
        await EnsureInterviewSchemaAsync(db);

        // The first placeholder set was a shared cross-college feed; communities are now private per college.
        if (await db.Institutions.AnyAsync(i => i.Name == "Narmada Skills Academy"))
        {
            await db.PostUpvotes.ExecuteDeleteAsync();
            await db.PostComments.ExecuteDeleteAsync();
            await db.CommunityPosts.ExecuteDeleteAsync();
            await db.Institutions.ExecuteDeleteAsync();
        }

        if (!await db.Institutions.AnyAsync())
        {
            await SeedCommunityAsync(db);
        }

        await ExamCatalogue.SyncAsync(db);

        await SeedDemoInstituteAsync(db, users);
        await SeedFirstAdminAsync(users);
    }

    /// <summary>Without this there is no way into the admin head, since admins are never self-registered.</summary>
    private static async Task SeedFirstAdminAsync(UserManager<AppUser> users)
    {
        if (await users.FindByEmailAsync(DemoAdminEmail) is not null)
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = DemoAdminEmail,
            Email = DemoAdminEmail,
            EmailConfirmed = true,
            FullName = "Sapling Admin",
            AccountType = AccountTypes.Admin,
        };

        await users.CreateAsync(admin, DemoPassword);
    }

    /// <summary>Gives the seeded demo college a sign-in so the institute head can be tried out.</summary>
    private static async Task SeedDemoInstituteAsync(SaplingDbContext db, UserManager<AppUser> users)
    {
        var college = await db.Institutions.FirstOrDefaultAsync(i => i.Name == DemoCollege);
        if (college is null || !string.IsNullOrEmpty(college.UserId))
        {
            return;
        }

        var user = await users.FindByEmailAsync(DemoInstituteEmail);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = DemoInstituteEmail,
                Email = DemoInstituteEmail,
                EmailConfirmed = true,
                FullName = "SGSITS Placement Cell",
                AccountType = AccountTypes.Institution,
            };

            if (!(await users.CreateAsync(user, DemoPassword)).Succeeded)
            {
                return;
            }
        }

        college.UserId = user.Id;
        college.State = "Madhya Pradesh";
        college.ContactEmail = DemoInstituteEmail;
        college.About = "Training and Placement Cell, SGSITS Indore.";

        // One pinned post so the student feed shows the pin ordering straight away.
        var newest = await db.CommunityPosts
            .Where(p => p.InstitutionId == college.Id)
            .OrderByDescending(p => p.PostedAtUtc)
            .FirstOrDefaultAsync();

        if (newest is not null)
        {
            newest.IsPinned = true;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Older databases hold the three hand-written demo roles. This adds the O*NET-based columns and the course
    /// link table, and drops the unsourced columns (salary, demand, employers, outlook, fixed fit score).
    /// </summary>
    private static async Task EnsureCareerSchemaAsync(SaplingDbContext db)
    {
        var roleColumns = await ColumnsAsync(db, "CareerRoles");
        (string Name, string Sql)[] added =
        [
            ("OnetCode", "TEXT NOT NULL DEFAULT ''"), ("OnetTitle", "TEXT NOT NULL DEFAULT ''"),
            ("Description", "TEXT NOT NULL DEFAULT ''"), ("Tasks", "TEXT NOT NULL DEFAULT ''"),
            ("AlsoCalled", "TEXT NOT NULL DEFAULT ''"), ("Technologies", "TEXT NOT NULL DEFAULT ''"),
            ("JobZone", "INTEGER NOT NULL DEFAULT 0"), ("Outlook", "TEXT NOT NULL DEFAULT ''"), ("Preparation", "TEXT NOT NULL DEFAULT ''"),
            ("Education", "TEXT NOT NULL DEFAULT ''"), ("Interests", "TEXT NOT NULL DEFAULT ''"),
            ("RelatedCodes", "TEXT NOT NULL DEFAULT ''"),
        ];

        foreach (var (name, sql) in added.Where(c => !roleColumns.Contains(c.Name)))
        {
            var alter = $"ALTER TABLE CareerRoles ADD COLUMN {name} {sql};";
            await db.Database.ExecuteSqlRawAsync(alter);
        }

        string[] dropped = ["Tier", "FitScore", "EntrySalaryMp", "EntrySalaryMetro", "DemandTrend", "FiveYearOutlook", "Employers", "Reasons", "CoreSkills", "CounterCase"];
        foreach (var name in dropped.Where(roleColumns.Contains))
        {
            var drop = $"ALTER TABLE CareerRoles DROP COLUMN {name};";
            await db.Database.ExecuteSqlRawAsync(drop);
        }

        // Skills are simply claimed or not; levels, verification and required levels were removed.
        foreach (var (table, column) in new[] { ("StudentSkills", "Level"), ("StudentSkills", "Verified"), ("StudentSkills", "Source"), ("RoleSkillRequirements", "RequiredLevel") })
        {
            if ((await ColumnsAsync(db, table)).Contains(column))
            {
                var dropColumn = $"ALTER TABLE {table} DROP COLUMN {column};";
                await db.Database.ExecuteSqlRawAsync(dropColumn);
            }
        }

        // The hand-written weekly plan and its demo courses gave way to NPTEL courses with checkpoints.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS RoadmapItems;");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS Courses;");
        // The first roadmap version stored NPTEL-only courses; rename in place so students keep their ticks.
        if ((await ColumnsAsync(db, "NptelCourses")).Count > 0 && (await ColumnsAsync(db, "LearningCourses")).Count == 0)
        {
            string[] renames =
            [
                "ALTER TABLE NptelCourses RENAME TO LearningCourses;",
                "ALTER TABLE LearningCourses RENAME COLUMN NptelId TO ExternalId;",
                "ALTER TABLE LearningCourses RENAME COLUMN Lectures TO Lessons;",
                "ALTER TABLE LearningCourses DROP COLUMN Professor;",
                "ALTER TABLE LearningCourses DROP COLUMN Institute;",
                "ALTER TABLE LearningCourses ADD COLUMN Provider TEXT NOT NULL DEFAULT 'NPTEL';",
                "ALTER TABLE LearningCourses ADD COLUMN Byline TEXT NOT NULL DEFAULT '';",
                "ALTER TABLE LearningCourses ADD COLUMN Minutes INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE CourseCheckpoints RENAME COLUMN NptelCourseId TO LearningCourseId;",
                "ALTER TABLE CourseCheckpoints RENAME COLUMN Lectures TO Lessons;",
                "ALTER TABLE CourseCheckpoints ADD COLUMN Minutes INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE SkillCourses RENAME COLUMN NptelCourseId TO LearningCourseId;",
            ];
            foreach (var sql in renames)
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "LearningCourses" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_LearningCourses" PRIMARY KEY AUTOINCREMENT,
                "Provider" TEXT NOT NULL,
                "ExternalId" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Byline" TEXT NOT NULL,
                "Url" TEXT NOT NULL,
                "Lessons" INTEGER NOT NULL,
                "Minutes" INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LearningCourses_ExternalId" ON "LearningCourses" ("ExternalId");
            CREATE TABLE IF NOT EXISTS "CourseCheckpoints" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CourseCheckpoints" PRIMARY KEY AUTOINCREMENT,
                "LearningCourseId" INTEGER NOT NULL,
                "Order" INTEGER NOT NULL,
                "Title" TEXT NOT NULL,
                "Lessons" INTEGER NOT NULL,
                "Minutes" INTEGER NOT NULL,
                CONSTRAINT "FK_CourseCheckpoints_LearningCourses_LearningCourseId" FOREIGN KEY ("LearningCourseId") REFERENCES "LearningCourses" ("Id") ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS "SkillCourses" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SkillCourses" PRIMARY KEY AUTOINCREMENT,
                "SkillId" INTEGER NOT NULL,
                "LearningCourseId" INTEGER NOT NULL,
                "Match" TEXT NOT NULL,
                CONSTRAINT "FK_SkillCourses_LearningCourses_LearningCourseId" FOREIGN KEY ("LearningCourseId") REFERENCES "LearningCourses" ("Id") ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS "CheckpointProgress" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CheckpointProgress" PRIMARY KEY AUTOINCREMENT,
                "StudentProfileId" INTEGER NOT NULL,
                "CourseCheckpointId" INTEGER NOT NULL,
                "CompletedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_CheckpointProgress_CourseCheckpoints_CourseCheckpointId" FOREIGN KEY ("CourseCheckpointId") REFERENCES "CourseCheckpoints" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_CheckpointProgress_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CheckpointProgress_StudentProfileId_CourseCheckpointId" ON "CheckpointProgress" ("StudentProfileId", "CourseCheckpointId");
            CREATE TABLE IF NOT EXISTS "StudentCourseChoices" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_StudentCourseChoices" PRIMARY KEY AUTOINCREMENT,
                "StudentProfileId" INTEGER NOT NULL,
                "SkillId" INTEGER NOT NULL,
                "LearningCourseId" INTEGER NOT NULL,
                CONSTRAINT "FK_StudentCourseChoices_LearningCourses_LearningCourseId" FOREIGN KEY ("LearningCourseId") REFERENCES "LearningCourses" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_StudentCourseChoices_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudentCourseChoices_StudentProfileId_SkillId" ON "StudentCourseChoices" ("StudentProfileId", "SkillId");
            """);

        if (!(await ColumnsAsync(db, "StudentProfiles")).Contains("TargetChosen"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE StudentProfiles ADD COLUMN TargetChosen INTEGER NOT NULL DEFAULT 0;");
        }

        // The Employability Score was removed; role fit is the only score now.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS ScoreSnapshots;");

        if (!(await ColumnsAsync(db, "RoleSkillRequirements")).Contains("Kind"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE RoleSkillRequirements ADD COLUMN Kind TEXT NOT NULL DEFAULT 'Technology';");
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CourseRoleLinks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CourseRoleLinks" PRIMARY KEY AUTOINCREMENT,
                "CareerRoleId" INTEGER NOT NULL,
                "Course" TEXT NOT NULL,
                "Branch" TEXT NOT NULL,
                "Relevance" TEXT NOT NULL,
                CONSTRAINT "FK_CourseRoleLinks_CareerRoles_CareerRoleId" FOREIGN KEY ("CareerRoleId") REFERENCES "CareerRoles" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_CourseRoleLinks_CareerRoleId" ON "CourseRoleLinks" ("CareerRoleId");
            CREATE INDEX IF NOT EXISTS "IX_CareerRoles_OnetCode" ON "CareerRoles" ("OnetCode");
            """);
    }

    /// <summary>
    /// The suggestion-list resume page was replaced by saved LaTeX resumes. Older databases get the new table and
    /// lose the old suggestions table and ATS columns.
    /// </summary>
    private static async Task EnsureResumeSchemaAsync(SaplingDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(ResumeTablesSql);

        var profileColumns = await ColumnsAsync(db, "StudentProfiles");
        foreach (var column in new[] { "AtsScore", "PreviousAtsScore", "ResumeTailoredForRole" }.Where(profileColumns.Contains))
        {
            var drop = $"ALTER TABLE StudentProfiles DROP COLUMN {column};";
            await db.Database.ExecuteSqlRawAsync(drop);
        }
    }

    private const string ResumeTablesSql = """
        DROP TABLE IF EXISTS "ResumeSuggestions";
        CREATE TABLE IF NOT EXISTS "StudentResumes" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StudentResumes" PRIMARY KEY AUTOINCREMENT,
            "StudentProfileId" INTEGER NOT NULL,
            "Title" TEXT NOT NULL,
            "Template" TEXT NOT NULL,
            "Source" TEXT NOT NULL,
            "SourceFileName" TEXT NULL,
            "SourceText" TEXT NULL,
            "Latex" TEXT NOT NULL,
            "DataJson" TEXT NULL,
            "Score" INTEGER NULL,
            "AnalysisJson" TEXT NULL,
            "AnalysedAtUtc" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "UpdatedAtUtc" TEXT NOT NULL,
            CONSTRAINT "FK_StudentResumes_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_StudentResumes_StudentProfileId_UpdatedAtUtc" ON "StudentResumes" ("StudentProfileId", "UpdatedAtUtc");
        """;

    private static async Task<HashSet<string>> ColumnsAsync(SaplingDbContext db, string table) =>
        (await db.Database.SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({table})").ToListAsync())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Columns the Exams calendar added to the table EnsureCreated built before they existed.</summary>
    private static async Task EnsureExamSchemaAsync(SaplingDbContext db)
    {
        var columns = await ColumnsAsync(db, "GovtExams");
        (string Name, string Sql)[] added =
        [
            ("OfficialUrl", "TEXT NOT NULL DEFAULT ''"),
            ("ExamEndsOn", "TEXT NULL"),
            ("DatesTentative", "INTEGER NOT NULL DEFAULT 0"),
            ("OpenYearsBeforeGraduation", "INTEGER NOT NULL DEFAULT 0"),
        ];

        foreach (var (name, sql) in added.Where(c => !columns.Contains(c.Name)))
        {
            // Identifiers cannot be parameterised in DDL; every value here is a constant from the list above.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"""ALTER TABLE "GovtExams" ADD COLUMN "{name}" {sql};""");
#pragma warning restore EF1002
        }
    }

    /// <summary>Columns the institute accounts added to tables EnsureCreated built before they existed.</summary>
    private static async Task EnsureAccountSchemaAsync(SaplingDbContext db)
    {
        var studentColumns = await ColumnsAsync(db, "StudentProfiles");
        if (!studentColumns.Contains("CreatedAtUtc"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "StudentProfiles" ADD COLUMN "CreatedAtUtc" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';""");
        }

        var userColumns = await ColumnsAsync(db, "AspNetUsers");
        if (!userColumns.Contains("AccountType"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "AspNetUsers" ADD COLUMN "AccountType" TEXT NOT NULL DEFAULT 'student';""");
        }

        if (!userColumns.Contains("CreatedAtUtc"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "AspNetUsers" ADD COLUMN "CreatedAtUtc" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';""");
        }
    }

    /// <summary>EnsureCreated skips databases that already exist, so community tables are added here for older demo DBs.</summary>
    private static async Task EnsureCommunitySchemaAsync(SaplingDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(CommunityTablesSql);

        var postColumns = await ColumnsAsync(db, "CommunityPosts");
        (string Table, string Name, string Sql)[] added =
        [
            // Added after the first community build, so tables created by it lack them.
            ("CommunityPosts", "ImageUrl", "TEXT NULL"),
            ("CommunityPosts", "IsPinned", "INTEGER NOT NULL DEFAULT 0"),
            ("CommunityPosts", "IsArchived", "INTEGER NOT NULL DEFAULT 0"),
            ("CommunityPosts", "UpdatedAtUtc", "TEXT NULL"),
            // Institution sign-in and branding came later still.
            ("Institutions", "UserId", "TEXT NULL"),
            ("Institutions", "LogoDataUrl", "TEXT NULL"),
            ("Institutions", "State", "TEXT NOT NULL DEFAULT ''"),
            ("Institutions", "About", "TEXT NOT NULL DEFAULT ''"),
            ("Institutions", "Website", "TEXT NOT NULL DEFAULT ''"),
            ("Institutions", "ContactEmail", "TEXT NOT NULL DEFAULT ''"),
            ("Institutions", "CreatedAtUtc", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
        ];

        var institutionColumns = await ColumnsAsync(db, "Institutions");

        foreach (var (table, name, sql) in added)
        {
            var existing = table == "CommunityPosts" ? postColumns : institutionColumns;
            if (!existing.Contains(name))
            {
                // Identifiers cannot be parameterised in DDL; every value here is a constant from the list above.
#pragma warning disable EF1002
                await db.Database.ExecuteSqlRawAsync($"""ALTER TABLE "{table}" ADD COLUMN "{name}" {sql};""");
#pragma warning restore EF1002
            }
        }

        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Institutions_UserId" ON "Institutions" ("UserId");""");

        // The first student-space build created the table without the edit stamp.
        if (!(await ColumnsAsync(db, "StudentMessages")).Contains("EditedAtUtc"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "StudentMessages" ADD COLUMN "EditedAtUtc" TEXT NULL;""");
        }
    }

    /// <summary>
    /// The voice interview replaced the text chat, so its old tables are dropped and the new ones created for
    /// databases that EnsureCreated has already built.
    /// </summary>
    private static async Task EnsureInterviewSchemaAsync(SaplingDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(InterviewTablesSql);

        // Added after the first voice-interview build, so tables created by it lack them.
        if (!(await ColumnsAsync(db, "MockInterviews")).Contains("TargetMinutes"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "MockInterviews" ADD COLUMN "TargetMinutes" INTEGER NOT NULL DEFAULT 20;""");
        }

        var turnColumns = await ColumnsAsync(db, "MockInterviewTurns");
        if (!turnColumns.Contains("AssessmentScore"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "MockInterviewTurns" ADD COLUMN "AssessmentScore" INTEGER NULL;""");
        }

        if (!turnColumns.Contains("AssessmentNote"))
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "MockInterviewTurns" ADD COLUMN "AssessmentNote" TEXT NULL;""");
        }
    }

    private const string InterviewTablesSql = """
            DROP TABLE IF EXISTS "InterviewTurns";
            DROP TABLE IF EXISTS "InterviewSessions";
            CREATE TABLE IF NOT EXISTS "MockInterviews" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MockInterviews" PRIMARY KEY AUTOINCREMENT,
                "StudentProfileId" INTEGER NOT NULL,
                "CareerRoleId" INTEGER NULL,
                "RoleTitle" TEXT NOT NULL,
                "Instructions" TEXT NOT NULL,
                "ResumeText" TEXT NULL,
                "ResumeFileName" TEXT NULL,
                "Status" TEXT NOT NULL,
                "Phase" TEXT NOT NULL,
                "QuestionLimit" INTEGER NOT NULL,
                "TargetMinutes" INTEGER NOT NULL DEFAULT 20,
                "CreatedAt" TEXT NOT NULL,
                "StartedAt" TEXT NULL,
                "EndedAt" TEXT NULL,
                "ReportJson" TEXT NULL,
                "OverallScore" INTEGER NULL,
                CONSTRAINT "FK_MockInterviews_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_MockInterviews_StudentProfileId_CreatedAt" ON "MockInterviews" ("StudentProfileId", "CreatedAt");
            CREATE TABLE IF NOT EXISTS "MockInterviewTurns" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MockInterviewTurns" PRIMARY KEY AUTOINCREMENT,
                "MockInterviewId" INTEGER NOT NULL,
                "Order" INTEGER NOT NULL,
                "Speaker" TEXT NOT NULL,
                "Phase" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "At" TEXT NOT NULL,
                "ResponseDelayMs" INTEGER NOT NULL,
                "SpeakingMs" INTEGER NOT NULL,
                "LongPauses" INTEGER NOT NULL,
                "LongestPauseMs" INTEGER NOT NULL,
                "WordCount" INTEGER NOT NULL,
                "FillerCount" INTEGER NOT NULL,
                "Typed" INTEGER NOT NULL,
                "AssessmentScore" INTEGER NULL,
                "AssessmentNote" TEXT NULL,
                CONSTRAINT "FK_MockInterviewTurns_MockInterviews_MockInterviewId" FOREIGN KEY ("MockInterviewId") REFERENCES "MockInterviews" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_MockInterviewTurns_MockInterviewId" ON "MockInterviewTurns" ("MockInterviewId");
            """;

    private const string CommunityTablesSql = """
        CREATE TABLE IF NOT EXISTS "Institutions" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Institutions" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL,
            "ShortName" TEXT NOT NULL,
            "City" TEXT NOT NULL,
            "Verified" INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS "CommunityPosts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_CommunityPosts" PRIMARY KEY AUTOINCREMENT,
            "InstitutionId" INTEGER NOT NULL,
            "Kind" TEXT NOT NULL,
            "Title" TEXT NOT NULL,
            "Body" TEXT NOT NULL,
            "PostedAtUtc" TEXT NOT NULL,
            "StartsAtUtc" TEXT NULL,
            "Venue" TEXT NULL,
            "CtaLabel" TEXT NULL,
            "CtaUrl" TEXT NULL,
            "ImageUrl" TEXT NULL,
            "Tags" TEXT NOT NULL,
            "BaseUpvotes" INTEGER NOT NULL,
            CONSTRAINT "FK_CommunityPosts_Institutions_InstitutionId" FOREIGN KEY ("InstitutionId") REFERENCES "Institutions" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_CommunityPosts_InstitutionId" ON "CommunityPosts" ("InstitutionId");
        CREATE INDEX IF NOT EXISTS "IX_CommunityPosts_PostedAtUtc" ON "CommunityPosts" ("PostedAtUtc");
        CREATE TABLE IF NOT EXISTS "PostUpvotes" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_PostUpvotes" PRIMARY KEY AUTOINCREMENT,
            "CommunityPostId" INTEGER NOT NULL,
            "StudentProfileId" INTEGER NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            CONSTRAINT "FK_PostUpvotes_CommunityPosts_CommunityPostId" FOREIGN KEY ("CommunityPostId") REFERENCES "CommunityPosts" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_PostUpvotes_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PostUpvotes_CommunityPostId_StudentProfileId" ON "PostUpvotes" ("CommunityPostId", "StudentProfileId");
        CREATE INDEX IF NOT EXISTS "IX_PostUpvotes_StudentProfileId" ON "PostUpvotes" ("StudentProfileId");
        CREATE TABLE IF NOT EXISTS "PostComments" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_PostComments" PRIMARY KEY AUTOINCREMENT,
            "CommunityPostId" INTEGER NOT NULL,
            "ParentCommentId" INTEGER NULL,
            "StudentProfileId" INTEGER NULL,
            "InstitutionId" INTEGER NULL,
            "AuthorName" TEXT NOT NULL,
            "AuthorHeadline" TEXT NULL,
            "Body" TEXT NOT NULL,
            "PostedAtUtc" TEXT NOT NULL,
            CONSTRAINT "FK_PostComments_CommunityPosts_CommunityPostId" FOREIGN KEY ("CommunityPostId") REFERENCES "CommunityPosts" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_PostComments_Institutions_InstitutionId" FOREIGN KEY ("InstitutionId") REFERENCES "Institutions" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_PostComments_PostComments_ParentCommentId" FOREIGN KEY ("ParentCommentId") REFERENCES "PostComments" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_PostComments_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_PostComments_CommunityPostId" ON "PostComments" ("CommunityPostId");
        CREATE INDEX IF NOT EXISTS "IX_PostComments_InstitutionId" ON "PostComments" ("InstitutionId");
        CREATE INDEX IF NOT EXISTS "IX_PostComments_ParentCommentId" ON "PostComments" ("ParentCommentId");
        CREATE INDEX IF NOT EXISTS "IX_PostComments_StudentProfileId" ON "PostComments" ("StudentProfileId");
        CREATE TABLE IF NOT EXISTS "SavedPosts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_SavedPosts" PRIMARY KEY AUTOINCREMENT,
            "CommunityPostId" INTEGER NOT NULL,
            "StudentProfileId" INTEGER NOT NULL,
            "SavedAtUtc" TEXT NOT NULL,
            CONSTRAINT "FK_SavedPosts_CommunityPosts_CommunityPostId" FOREIGN KEY ("CommunityPostId") REFERENCES "CommunityPosts" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_SavedPosts_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SavedPosts_CommunityPostId_StudentProfileId" ON "SavedPosts" ("CommunityPostId", "StudentProfileId");
        CREATE INDEX IF NOT EXISTS "IX_SavedPosts_StudentProfileId" ON "SavedPosts" ("StudentProfileId");
        CREATE TABLE IF NOT EXISTS "StudentMessages" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_StudentMessages" PRIMARY KEY AUTOINCREMENT,
            "College" TEXT NOT NULL,
            "StudentProfileId" INTEGER NOT NULL,
            "Body" TEXT NOT NULL,
            "PostedAtUtc" TEXT NOT NULL,
            "EditedAtUtc" TEXT NULL,
            CONSTRAINT "FK_StudentMessages_StudentProfiles_StudentProfileId" FOREIGN KEY ("StudentProfileId") REFERENCES "StudentProfiles" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_StudentMessages_College_Id" ON "StudentMessages" ("College", "Id");
        CREATE INDEX IF NOT EXISTS "IX_StudentMessages_StudentProfileId" ON "StudentMessages" ("StudentProfileId");
        CREATE TABLE IF NOT EXISTS "SavedStudentMessages" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_SavedStudentMessages" PRIMARY KEY AUTOINCREMENT,
            "StudentMessageId" INTEGER NOT NULL,
            "StudentProfileId" INTEGER NOT NULL,
            "SavedAtUtc" TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SavedStudentMessages_StudentMessageId_StudentProfileId" ON "SavedStudentMessages" ("StudentMessageId", "StudentProfileId");
        """;

    /// <summary>
    /// Placeholder college posts until institution accounts exist. Names match the AICTE catalogue, which is how a
    /// student's profile college is linked to its private community. Dates are relative so the feed looks current.
    /// </summary>
    private static async Task SeedCommunityAsync(SaplingDbContext db)
    {
        var sgsits = new Institution { Name = "Shri Govindram Seksaria Institute of Technology and Science", ShortName = "SGSITS", City = "Indore", Verified = true };
        var iiti = new Institution { Name = "Indian Institute of Technology Indore", ShortName = "IIT Indore", City = "Indore", Verified = true };
        var manit = new Institution { Name = "Maulana Azad National Institute of Technology Bhopal", ShortName = "MANIT", City = "Bhopal", Verified = true };
        db.Institutions.AddRange(sgsits, iiti, manit);

        var now = DateTime.UtcNow;

        // A time of day in IST, some days from today.
        DateTime At(int days, double istHour = 10) => now.Date.AddDays(days).AddHours(istHour - 5.5);

        var docker = new CommunityPost
        {
            Institution = sgsits, Kind = "Workshop", PostedAtUtc = now.AddHours(-3), BaseUpvotes = 86,
            Title = "Two-day hands-on workshop: Docker and cloud fundamentals",
            Body = "The Department of Computer Engineering is running a two-day, lab-first workshop on containers and cloud basics. Day one covers Linux essentials, images, containers and Docker Compose. Day two deploys a small Java and PostgreSQL app to a free-tier cloud account.\n\nBring a laptop with at least 8 GB RAM. Seats are limited to 60 and allotted first come, first served.",
            StartsAtUtc = At(5), Venue = "Seminar Hall, CE Block, SGSITS Indore",
            Tags = "Docker|Cloud fundamentals|Linux|Hands-on",
        };
        var hackathon = new CommunityPost
        {
            Institution = manit, Kind = "Event", PostedAtUtc = now.AddHours(-20), BaseUpvotes = 214,
            Title = "Hack Bhopal: 36-hour hackathon for MANIT students",
            Body = "Teams of up to four MANIT students can take part, from any branch or year. Problem statements come from state departments and local startups: agriculture supply chains, public transport, and Hindi-first citizen services.\n\nMeals are covered for all 36 hours. The top three teams receive incubation support from the institute's innovation cell.",
            StartsAtUtc = At(18, 9), Venue = "MANIT campus, Bhopal",
            Tags = "Hackathon|Teams of 4|Incubation",
        };
        var research = new CommunityPost
        {
            Institution = iiti, Kind = "Opportunity", PostedAtUtc = now.AddDays(-1).AddHours(-4), BaseUpvotes = 172,
            Title = "Summer research projects open to pre-final year undergraduates",
            Body = "Eight-week faculty-mentored research projects across computer science, electrical and civil engineering. Pre-final year students may apply with a one-page statement of interest and a faculty reference.\n\nA monthly stipend is paid for the duration of the project.",
            Tags = "Research|Internship|Stipend|Pre-final year",
        };
        var nptel = new CommunityPost
        {
            Institution = sgsits, Kind = "Announcement", PostedAtUtc = now.AddDays(-2), BaseUpvotes = 64,
            Title = "NPTEL local chapter: exam registration closes this Friday",
            Body = "Students enrolled in NPTEL courses through the SGSITS local chapter must register for the proctored exam by Friday. A passed exam gives you a certificate that counts towards verified skills and certifications on most placement portals.",
            CtaLabel = "Open NPTEL", CtaUrl = "https://nptel.ac.in",
            Tags = "NPTEL|Certification|Deadline",
        };
        var mockDay = new CommunityPost
        {
            Institution = sgsits, Kind = "Workshop", PostedAtUtc = now.AddDays(-3), BaseUpvotes = 131,
            Title = "Mock interview day with alumni from product companies",
            Body = "SGSITS alumni working as software engineers will run 30-minute mock technical interviews over video, followed by written feedback. Open to final-year students from any branch. Pick a slot in the morning or afternoon session.",
            StartsAtUtc = At(9, 11), Venue = "Online",
            Tags = "Interviews|Alumni|Online",
        };
        var drive = new CommunityPost
        {
            Institution = sgsits, Kind = "Opportunity", PostedAtUtc = now.AddDays(-4), BaseUpvotes = 98,
            Title = "Pre-placement talk and campus drive for final-year CSE and IT",
            Body = "The Training and Placement Cell is hosting a pre-placement talk followed by an online assessment for the software trainee role. Eligibility: 6.5 CGPA and no active backlogs. Carry two printed copies of your resume.",
            StartsAtUtc = At(6, 14), Venue = "Main Auditorium, SGSITS Indore",
            Tags = "Placement|Final year|CSE|IT",
        };
        var aicte = new CommunityPost
        {
            Institution = manit, Kind = "Opportunity", PostedAtUtc = now.AddDays(-5), BaseUpvotes = 57,
            Title = "New Madhya Pradesh listings on the AICTE internship portal",
            Body = "Our placement office has shortlisted internships on the AICTE portal that are based in Indore, Bhopal and Jabalpur, or fully remote. Most are open to second and third-year students and several are paid.",
            CtaLabel = "Browse internships", CtaUrl = "https://internship.aicte-india.org",
            Tags = "AICTE|Internship|Remote",
        };
        var lecture = new CommunityPost
        {
            Institution = iiti, Kind = "Event", PostedAtUtc = now.AddDays(-6), BaseUpvotes = 143,
            Title = "Guest lecture: what recruiters actually look at in your GitHub",
            Body = "An engineering manager walks through real student profiles, anonymised, and explains what gets a resume shortlisted: README quality, commit history, tests, and one project done properly rather than ten tutorials.",
            StartsAtUtc = At(3, 17), Venue = "Online",
            Tags = "GitHub|Portfolio|Guest lecture",
        };
        var english = new CommunityPost
        {
            Institution = sgsits, Kind = "Workshop", PostedAtUtc = now.AddDays(-8), BaseUpvotes = 47,
            Title = "Spoken English and interview confidence batch",
            Body = "The Training and Placement Cell is running a free four-week evening batch for students who are more comfortable in Hindi. Practise answering common HR questions in English, in small groups of ten.",
            StartsAtUtc = At(12, 17), Venue = "Room 204, Main Building, SGSITS Indore",
            Tags = "Communication|Free|Hindi-friendly",
        };

        var sprint = new CommunityPost
        {
            Institution = sgsits, Kind = "Event", PostedAtUtc = now.AddHours(-26), BaseUpvotes = 121,
            Title = "Code Sprint: 24-hour campus hackathon",
            Body = "Teams of up to four SGSITS students, from any branch or year. Problem statements come from Indore startups and the municipal corporation: waste collection routing, bus arrival times, and a Hindi-first grievance app.\n\nThe top three teams present to the startups, and two of them have offered internships to winning teams in past editions.",
            StartsAtUtc = At(16, 9), Venue = "Central Library, SGSITS Indore",
            Tags = "Hackathon|Teams of 4|Internships",
        };

        db.CommunityPosts.AddRange(docker, sprint, hackathon, research, nptel, mockDay, drive, aicte, lecture, english);
        await db.SaveChangesAsync();

        PostComment Student(CommunityPost post, string name, string headline, string body, double hoursAfter, PostComment? parent = null) => new()
        {
            CommunityPostId = post.Id, ParentCommentId = parent?.Id, AuthorName = name, AuthorHeadline = headline, Body = body,
            PostedAtUtc = post.PostedAtUtc.AddHours(hoursAfter),
        };

        PostComment Reply(CommunityPost post, PostComment parent, string body, double hoursAfter) => new()
        {
            CommunityPostId = post.Id, ParentCommentId = parent.Id, InstitutionId = post.InstitutionId,
            AuthorName = post.Institution!.Name, Body = body, PostedAtUtc = post.PostedAtUtc.AddHours(hoursAfter),
        };

        var dockerYears = Student(docker, "Riya Sharma", "Information Technology · 2027 batch", "Is this open to second-year students or only final year?", 0.5);
        var dockerCert = Student(docker, "Mohit Patel", "Computer Science & Engineering · 2026 batch", "Will there be a certificate? It would help for cloud support roles.", 1.2);
        var hackCross = Student(hackathon, "Ananya Tiwari", "Electrical Engineering · 2027 batch", "Can first-year students take part?", 2);
        var sprintBranch = Student(sprint, "Sneha Joshi", "Electronics & Communication · 2026 batch", "Can teams mix branches?", 2);
        var hackTeam = Student(hackathon, "Arjun Singh", "Computer Science & Engineering · 2027 batch", "Looking for a frontend developer to join our team. We are taking on the public transport problem.", 6);
        var mockRecord = Student(mockDay, "Kavya Rao", "Mechanical Engineering · 2026 batch", "Please share a recording of a sample interview for those who cannot get a slot.", 3);
        var englishFee = Student(english, "Pooja Yadav", "Civil Engineering · 2026 batch", "Is there a weekend batch as well? I have labs on weekday evenings.", 10);
        var researchCgpa = Student(research, "Devansh Mishra", "Electrical Engineering · 2027 batch", "Is there a minimum CGPA to apply?", 5);

        db.PostComments.AddRange(dockerYears, dockerCert, sprintBranch, hackCross, hackTeam, mockRecord, englishFee, researchCgpa);
        await db.SaveChangesAsync();

        db.PostComments.AddRange(
            Reply(docker, dockerYears, "Open to all years. Second-year students are welcome as long as they bring a laptop that meets the requirement.", 1),
            Student(docker, "Aditi Chouhan", "Information Technology · 2027 batch", "Thanks, registering now.", 1.5, dockerYears),
            Reply(docker, dockerCert, "Yes. Everyone who completes both days' labs receives a participation certificate from the department.", 2),
            Reply(sprint, sprintBranch, "Yes, and we encourage it. At most two members of a team can be from the same branch.", 3),
            Reply(english, englishFee, "A Saturday morning batch starts the week after. We will post the details here.", 14),
            Reply(hackathon, hackCross, "Yes, all years are welcome. First-year teams are judged in their own category.", 3),
            Reply(mockDay, mockRecord, "A recorded sample interview will be shared on this post within 48 hours of the event.", 5),
            Reply(research, researchCgpa, "There is no hard cut-off. The statement of interest and faculty reference carry more weight than CGPA.", 9));
        await db.SaveChangesAsync();
    }

    /// <summary>Ensures any missing columns (such as State) are added to existing SQLite database.</summary>
    private static async Task EnsureSchemaColumnsAsync(SaplingDbContext db)
    {
        try
        {
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var conn = db.Database.GetDbConnection();
            var shouldClose = conn.State != System.Data.ConnectionState.Open;
            if (shouldClose)
            {
                await conn.OpenAsync();
            }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(StudentProfiles);";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingCols.Add(reader.GetString(1));
                }

                if (!existingCols.Contains("State"))
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE StudentProfiles ADD COLUMN State TEXT NOT NULL DEFAULT '';";
                    await alterCmd.ExecuteNonQueryAsync();
                }

                if (!existingCols.Contains("AvatarDataUrl"))
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE StudentProfiles ADD COLUMN AvatarDataUrl TEXT NULL;";
                    await alterCmd.ExecuteNonQueryAsync();
                }

                // Ensure Colleges table exists with Category and Aliases
                using var tableCmd = conn.CreateCommand();
                tableCmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS "Colleges" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Colleges" PRIMARY KEY AUTOINCREMENT,
                        "Name" TEXT NOT NULL,
                        "State" TEXT NOT NULL,
                        "City" TEXT NOT NULL,
                        "AicteId" TEXT NULL,
                        "Category" TEXT NOT NULL DEFAULT 'AICTE Approved',
                        "Aliases" TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS "IX_Colleges_Name" ON "Colleges" ("Name");
                    CREATE INDEX IF NOT EXISTS "IX_Colleges_State" ON "Colleges" ("State");
                    CREATE INDEX IF NOT EXISTS "IX_Colleges_City" ON "Colleges" ("City");
                    """;
                await tableCmd.ExecuteNonQueryAsync();

                // Ensure Category and Aliases columns exist if table was already created
                var collegeCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var colCmd = conn.CreateCommand();
                colCmd.CommandText = "PRAGMA table_info(Colleges);";
                using var colReader = await colCmd.ExecuteReaderAsync();
                while (await colReader.ReadAsync())
                {
                    collegeCols.Add(colReader.GetString(1));
                }

                if (!collegeCols.Contains("Category"))
                {
                    using var addCat = conn.CreateCommand();
                    addCat.CommandText = "ALTER TABLE Colleges ADD COLUMN Category TEXT NOT NULL DEFAULT 'AICTE Approved';";
                    await addCat.ExecuteNonQueryAsync();
                }

                if (!collegeCols.Contains("Aliases"))
                {
                    using var addAliases = conn.CreateCommand();
                    addAliases.CommandText = "ALTER TABLE Colleges ADD COLUMN Aliases TEXT NOT NULL DEFAULT '';";
                    await addAliases.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await conn.CloseAsync();
                }
            }
        }
        catch
        {
            // Non-SQLite or already applied
        }
    }

    /// <summary>Seeds all Indian colleges from the AICTE/AISHE dataset if the table is empty.</summary>
    private static async Task EnsureCollegesSeededAsync(SaplingDbContext db)
    {
        try
        {
            if (await db.Colleges.AnyAsync())
                return;

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Data", "aicte_colleges.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "aicte_colleges.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Sapling.Web", "Data", "aicte_colleges.json"),
                @"C:\Users\Abhijeet\source\repos\Sapling\Sapling.Web\Data\aicte_colleges.json",
            };

            var jsonPath = candidates.FirstOrDefault(File.Exists);
            if (jsonPath is null)
                return;

            await using var stream = File.OpenRead(jsonPath);
            var items = await System.Text.Json.JsonSerializer.DeserializeAsync<List<CollegeSeedItem>>(stream);
            if (items is not null && items.Count > 0)
            {
                const int chunkSize = 1500;
                for (int i = 0; i < items.Count; i += chunkSize)
                {
                    var chunk = items.Skip(i).Take(chunkSize)
                        .Select(c => new College 
                        { 
                            Name = c.name, 
                            State = c.state, 
                            City = c.city, 
                            AicteId = c.aicte_id,
                            Category = c.category ?? "AICTE Approved",
                            Aliases = c.aliases ?? ""
                        })
                        .ToList();
                    db.Colleges.AddRange(chunk);
                    await db.SaveChangesAsync();
                }
            }
        }
        catch
        {
            // Avoid startup failure if dataset file is inaccessible
        }
    }

    private sealed record CollegeSeedItem(string name, string state, string city, string? aicte_id, string? category = null, string? aliases = null);

    /// <summary>Adds any catalogue skills that are missing from an existing database (idempotent).</summary>
    private static async Task EnsureSkillCatalogueAsync(SaplingDbContext db)
    {
        var existingNames = await db.Skills.Select(s => s.Name).ToHashSetAsync();
        var toAdd = CatalogueSkills
            .Where(s => !existingNames.Contains(s.Name))
            .Select(s => new Skill { Name = s.Name, Category = s.Category })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Skills.AddRange(toAdd);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>All skills that should exist in the catalogue. Called on first run and also by EnsureSkillCatalogueAsync for existing DBs.</summary>
    private static readonly (string Name, string Category)[] CatalogueSkills =
    [
        // ── Programming Languages ──
        ("Python", "Programming"), ("Java", "Programming"), ("C", "Programming"),
        ("C++", "Programming"), ("C#", "Programming"), ("TypeScript", "Programming"),
        ("Kotlin", "Programming"), ("Go", "Programming"), ("R", "Programming"),
        ("PHP", "Programming"), ("Ruby", "Programming"), ("Swift", "Programming"),
        ("Dart", "Programming"), ("MATLAB", "Programming"),

        // ── Web Development ──
        ("HTML & CSS", "Web"), ("JavaScript", "Web"), ("React", "Web"),
        ("Angular", "Web"), ("Vue.js", "Web"), ("Next.js", "Web"),
        ("Node.js", "Web"), ("Tailwind CSS", "Web"), ("Bootstrap", "Web"),

        // ── Backend & APIs ──
        ("REST APIs", "Backend"), ("System design", "Backend"),
        ("Spring Boot", "Backend"), ("Django", "Backend"), ("Flask", "Backend"),
        ("Express.js", "Backend"), (".NET", "Backend"), ("GraphQL", "Backend"),
        ("Microservices", "Backend"),

        // ── Data & Analytics ──
        ("SQL", "Data"), ("Pandas", "Data"), ("Machine learning", "Data"),
        ("Statistics", "Data"), ("Data visualisation", "Data"), ("Excel", "Data"),
        ("Power BI", "Data"), ("Tableau", "Data"), ("NumPy", "Data"),
        ("TensorFlow", "Data"), ("Deep learning", "Data"), ("NLP", "Data"),
        ("Big Data (Spark)", "Data"),

        // ── Cloud & DevOps ──
        ("Cloud fundamentals", "Cloud"), ("Docker", "Cloud"),
        ("Kubernetes", "Cloud"), ("AWS", "Cloud"), ("Azure", "Cloud"),
        ("GCP", "Cloud"), ("CI/CD", "Cloud"), ("Terraform", "Cloud"),
        ("Jenkins", "Cloud"), ("Ansible", "Cloud"),

        // ── Tooling ──
        ("Git", "Tooling"), ("Linux", "Tooling"), ("VS Code", "Tooling"),
        ("IntelliJ", "Tooling"), ("Postman", "Tooling"), ("Jira", "Tooling"),
        ("Figma", "Tooling"),

        // ── Fundamentals ──
        ("Data structures", "Fundamentals"), ("Algorithms", "Fundamentals"),
        ("OOP", "Fundamentals"), ("DBMS", "Fundamentals"),
        ("Computer networks", "Fundamentals"), ("Operating systems", "Fundamentals"),

        // ── Professional Skills ──
        ("Communication", "Professional"), ("Aptitude", "Professional"),
        ("Leadership", "Professional"), ("Teamwork", "Professional"),
        ("Problem solving", "Professional"), ("Time management", "Professional"),
        ("Presentation skills", "Professional"),

        // ── Quality & Testing ──
        ("Testing", "Quality"), ("Unit testing", "Quality"), ("Selenium", "Quality"),
        ("JUnit", "Quality"), ("Manual testing", "Quality"), ("Performance testing", "Quality"),

        // ── Mobile Development ──
        ("Android", "Mobile"), ("iOS", "Mobile"), ("Flutter", "Mobile"),
        ("React Native", "Mobile"),

        // ── Cybersecurity ──
        ("Network security", "Cybersecurity"), ("Ethical hacking", "Cybersecurity"),
        ("Cryptography", "Cybersecurity"),

        // ── Commerce & Finance ──
        ("Tally", "Commerce"), ("GST", "Commerce"), ("Accounting", "Commerce"),
        ("Financial analysis", "Commerce"), ("SAP", "Commerce"),
        ("Business analytics", "Commerce"),

        // ── Government ──
        ("General studies", "Government"),
    ];

    private static async Task SeedCatalogueAsync(SaplingDbContext db)
    {
        var skills = CatalogueSkills.Select(s => new Skill { Name = s.Name, Category = s.Category }).ToArray();
        db.Skills.AddRange(skills);
        await db.SaveChangesAsync();


        var today = DateOnly.FromDateTime(DateTime.Today);
        db.Opportunities.AddRange(
            new Opportunity { Title = "Backend Engineering Intern", Company = "Persistent Systems", Kind = "Internship", Location = "Indore", Remote = false, Stipend = "₹18,000 / month", ClosesOn = today.AddDays(12), MatchScore = 74, MissingSkills = "Cloud fundamentals|Docker", Reasons = "Java and SQL match the stack|Located in your preferred city|Converts to a full-time offer for 6 in 10 interns", Description = "Six-month internship on payments infrastructure. You will work in a Java and PostgreSQL codebase with a mentor assigned from week one." },
            new Opportunity { Title = "Graduate Trainee — Software", Company = "Yash Technologies", Kind = "Full-time", Location = "Bhopal", Remote = false, Stipend = "₹4.2 LPA", ClosesOn = today.AddDays(3), MatchScore = 68, MissingSkills = "Cloud fundamentals", Reasons = "CGPA clears the 6.5 cut-off|Branch is eligible|One skill away from the stated requirement", Description = $"Campus drive for the {DateTime.Today.Year} batch. Online assessment, then a technical and an HR round on the same day." },
            new Opportunity { Title = "Data Analyst Intern", Company = "InfoBeans", Kind = "Internship", Location = "Indore", Remote = true, Stipend = "₹15,000 / month", ClosesOn = today.AddDays(21), MatchScore = 61, MissingSkills = "Pandas|Data visualisation", Reasons = "Statistics is your strongest subject|Remote, so no relocation cost|Team takes non-CS branches", Description = "Reporting and dashboard work for a retail client. SQL every day, Python two days a week." },
            new Opportunity { Title = "Junior Cloud Support Associate", Company = "Nagarro", Kind = "Full-time", Location = "Remote (India)", Remote = true, Stipend = "₹4.8 LPA", ClosesOn = today.AddDays(30), MatchScore = 48, MissingSkills = "Cloud fundamentals|Docker|Linux", Reasons = "Highest salary band open to you|Remote-first|Certification can substitute for experience", Description = "Support and automation for cloud workloads. A cloud practitioner certification is treated as equivalent to one year of experience." },
            new Opportunity { Title = "Software Engineer Trainee", Company = "TCS (NQT)", Kind = "Full-time", Location = "Indore / Bhopal", Remote = false, Stipend = "₹3.6 LPA", ClosesOn = today.AddDays(45), MatchScore = 71, MissingSkills = "", Reasons = "You meet every stated requirement|Largest single recruiter in the state|Aptitude is a scored round and you have four weeks to prepare", Description = "National Qualifier Test route. Aptitude, programming logic and coding sections, followed by a technical interview." },
            new Opportunity { Title = "Frontend Intern", Company = "Impetus", Kind = "Internship", Location = "Indore", Remote = false, Stipend = "₹12,000 / month", ClosesOn = today.AddDays(9), MatchScore = 52, MissingSkills = "React|JavaScript", Reasons = "HTML and CSS already in place|Short commute from campus|Small team, broad exposure", Description = "Build internal tools in React. Suitable if you want product work rather than services work." });

        var quiz = new[]
        {
            ("I enjoy taking apart a device to understand how it works.", "Realistic"),
            ("I would rather fix something physical than write about it.", "Realistic"),
            ("I like working with tools, machines or hardware.", "Realistic"),
            ("I enjoy finding the flaw in an argument or a dataset.", "Investigative"),
            ("I would happily spend an afternoon on one hard problem.", "Investigative"),
            ("I read about how things work even when no exam depends on it.", "Investigative"),
            ("I often think of how something could look or sound better.", "Artistic"),
            ("I prefer open-ended work to work with one correct answer.", "Artistic"),
            ("I enjoy designing interfaces, posters or presentations.", "Artistic"),
            ("People come to me when they need something explained.", "Social"),
            ("I enjoy teaching a junior something I have just learned.", "Social"),
            ("I would choose team work over solo work most of the time.", "Social"),
            ("I am comfortable persuading people to change their minds.", "Enterprising"),
            ("I would like to run something of my own one day.", "Enterprising"),
            ("I enjoy leading a group towards a deadline.", "Enterprising"),
            ("I like clear rules, checklists and predictable processes.", "Conventional"),
            ("I keep my notes and files organised without being told.", "Conventional"),
            ("I would rather improve an existing system than invent a new one.", "Conventional"),
        };
        db.QuizQuestions.AddRange(quiz.Select((q, i) => new QuizQuestion { Text = q.Item1, Dimension = q.Item2, Order = i }));

        await db.SaveChangesAsync();
    }

    private static async Task SeedDemoStudentAsync(SaplingDbContext db, UserManager<AppUser> users)
    {
        var user = new AppUser
        {
            UserName = DemoEmail,
            Email = DemoEmail,
            EmailConfirmed = true,
            FullName = "Aarav Verma",
        };

        var created = await users.CreateAsync(user, DemoPassword);
        if (!created.Succeeded)
        {
            return;
        }

        var softwareDeveloper = await db.CareerRoles.FirstAsync(r => r.OnetCode == "15-1252.00");
        var skillIds = await db.Skills.ToDictionaryAsync(s => s.Name, s => s.Id);

        var profile = new StudentProfile
        {
            UserId = user.Id,
            College = "Shri Govindram Seksaria Institute of Technology and Science",
            Branch = "Computer Science & Engineering",
            GraduationYear = DateTime.Today.Year,
            City = "Indore",
            State = "Madhya Pradesh",
            Cgpa = 7.2,
            Backlogs = 0,
            PreferredLanguage = "English",
            OnboardingComplete = true,
            RiasecCode = "IRC",
            TargetRoleId = softwareDeveloper.Id,
            TargetChosen = true,
        };

        string[] studentSkills =
        [
            "Java", "SQL", "Data structures", "Git", "HTML & CSS", "JavaScript", "Python",
            "Linux", "REST APIs", "Communication", "Aptitude", "Statistics",
        ];

        profile.Skills = studentSkills
            .Select(name => new StudentSkill { SkillId = skillIds[name] })
            .ToList();

        var year = DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var resumeData = new Sapling.Shared.Contracts.ResumeDataDto(
            new(user.FullName, DemoEmail, "", "Indore, Madhya Pradesh", "", "", ""),
            "Final-year Computer Science student at SGSITS Indore building backend services in Java and SQL.",
            [new("Shri Govindram Seksaria Institute of Technology and Science", "B.Tech", "Computer Science & Engineering", "", year, "CGPA 7.2/10", [])],
            [],
            [new("Library management system", "", "Java, SQL", "", "", ["Built a Java and SQL application to issue and return library books."])],
            [
                new("Languages", ["Java", "Python", "SQL", "JavaScript", "HTML & CSS"]),
                new("Tools", ["Git", "Linux", "REST APIs"]),
            ],
            []);

        profile.Resumes =
        [
            new StudentResume
            {
                Title = "My resume",
                Template = Sapling.Shared.Contracts.ResumeTemplates.SingleColumn,
                Source = "wizard",
                Latex = Services.LatexTemplates.Render(Sapling.Shared.Contracts.ResumeTemplates.SingleColumn, resumeData),
                DataJson = Ai.JsonOutput.Serialize(resumeData),
            },
        ];

        db.StudentProfiles.Add(profile);
        await db.SaveChangesAsync();
    }
}
