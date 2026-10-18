using Microsoft.EntityFrameworkCore;

namespace Sapling.Web.Data;

/// <summary>
/// Upcoming exams an engineering student would plan around, synced into the database on every start so
/// date changes ship with a deploy. Dates come from each body's published calendar; an exam without
/// announced dates keeps ExamOn null and shows as "dates awaited" rather than a guessed date.
/// </summary>
public static class ExamCatalogue
{
    public static IReadOnlyList<GovtExam> All { get; } =
    [
        new()
        {
            Name = "SSC CGL 2026 (Tier 1)", Authority = "Staff Selection Commission", Level = "Central",
            ExamOn = new(2026, 9, 30), ExamEndsOn = new(2026, 10, 30),
            MinAge = 18, MaxAge = 32, QualificationRequired = "Graduate",
            SyllabusAreas = "Quantitative aptitude|Reasoning|English|General awareness",
            Summary = "Group B and C posts across central ministries. Tier 1 is a computer-based screening paper.",
            OfficialUrl = "https://ssc.gov.in",
        },
        new()
        {
            Name = "IBPS PO 2026 (Mains)", Authority = "Institute of Banking Personnel Selection", Level = "Banking",
            ExamOn = new(2026, 10, 4), DatesTentative = true,
            MinAge = 20, MaxAge = 30, QualificationRequired = "Graduate",
            SyllabusAreas = "Reasoning|Quantitative aptitude|English|Banking awareness",
            Summary = "Probationary officer posts in public sector banks; the aptitude work overlaps campus placement preparation.",
            OfficialUrl = "https://www.ibps.in",
        },
        new()
        {
            Name = "IBPS SO 2026 (Mains)", Authority = "Institute of Banking Personnel Selection", Level = "Banking",
            ExamOn = new(2026, 11, 1), DatesTentative = true,
            MinAge = 20, MaxAge = 30, QualificationRequired = "Graduate",
            SyllabusAreas = "Professional knowledge|IT officer paper",
            Summary = "Specialist officer posts, including IT officer, where an engineering degree is the entry requirement.",
            OfficialUrl = "https://www.ibps.in",
        },
        new()
        {
            Name = "CAT 2026", Authority = "Indian Institutes of Management", Level = "Entrance",
            ExamOn = new(2026, 11, 29),
            QualificationRequired = "Graduate (final-year students may apply)", OpenYearsBeforeGraduation = 1,
            SyllabusAreas = "Verbal ability|Data interpretation|Logical reasoning|Quantitative ability",
            Summary = "Entrance to the IIMs and most MBA programmes. No age limit.",
            OfficialUrl = "https://iimcat.ac.in",
        },
        new()
        {
            Name = "UPSC ESE 2027 (Prelims)", Authority = "Union Public Service Commission", Level = "Central",
            NotificationOn = new(2026, 9, 16), ExamOn = new(2027, 1, 31),
            MinAge = 21, MaxAge = 30, QualificationRequired = "Engineering degree (final-year students may apply)", OpenYearsBeforeGraduation = 1,
            SyllabusAreas = "General studies|Engineering aptitude|Branch paper",
            Summary = "Engineering Services: Group A technical posts in railways, CPWD, telecom and defence.",
            OfficialUrl = "https://upsc.gov.in",
        },
        new()
        {
            Name = "GATE 2027", Authority = "IIT Madras", Level = "Entrance",
            NotificationOn = new(2026, 9, 2), ExamOn = new(2027, 2, 6), ExamEndsOn = new(2027, 2, 21),
            QualificationRequired = "Third-year undergraduate or above", OpenYearsBeforeGraduation = 2,
            SyllabusAreas = "Engineering mathematics|General aptitude|Branch paper",
            Summary = "Held over three weekends. The score opens M.Tech admissions and PSU recruitment. No age limit.",
            OfficialUrl = "https://gate2027.iitm.ac.in",
        },
        new()
        {
            Name = "UPSC Civil Services 2027 (Prelims)", Authority = "Union Public Service Commission", Level = "Central",
            NotificationOn = new(2027, 1, 13), ExamOn = new(2027, 5, 23),
            MinAge = 21, MaxAge = 32, QualificationRequired = "Graduate (final-year students may apply)", OpenYearsBeforeGraduation = 1,
            SyllabusAreas = "General studies|CSAT|Essay|Optional subject",
            Summary = "IAS, IPS, IFS and allied services. Mains follows in August for those who clear prelims.",
            OfficialUrl = "https://upsc.gov.in",
        },
        new()
        {
            Name = "RRB Junior Engineer", Authority = "Railway Recruitment Boards", Level = "Railways",
            MinAge = 18, MaxAge = 33, QualificationRequired = "Diploma or Degree in Engineering",
            SyllabusAreas = "Technical subject|Mathematics|Reasoning|General awareness",
            Summary = "Engineering-specific technical paper, which favours your branch over general-stream candidates.",
            OfficialUrl = "https://www.rrbapply.gov.in",
        },
        new()
        {
            Name = "SBI Probationary Officer", Authority = "State Bank of India", Level = "Banking",
            MinAge = 21, MaxAge = 30, QualificationRequired = "Graduate (final-year students may apply)", OpenYearsBeforeGraduation = 1,
            SyllabusAreas = "Reasoning|Quantitative aptitude|English|Data analysis",
            Summary = "India's largest bank recruits officers once a year through a three-phase exam.",
            OfficialUrl = "https://sbi.co.in/web/careers",
        },
        new()
        {
            Name = "RBI Grade B Officer", Authority = "Reserve Bank of India", Level = "Banking",
            MinAge = 21, MaxAge = 30, QualificationRequired = "Graduate",
            SyllabusAreas = "Economic and social issues|Finance and management|Reasoning|English",
            Summary = "Officer posts at the central bank; one of the best-paid banking entries for fresh graduates.",
            OfficialUrl = "https://opportunities.rbi.org.in",
        },
        new()
        {
            Name = "ISRO Scientist/Engineer 'SC'", Authority = "Indian Space Research Organisation", Level = "Central",
            MinAge = 18, MaxAge = 28, QualificationRequired = "Graduate",
            SyllabusAreas = "Branch paper|Interview",
            Summary = "Direct recruitment for electronics, mechanical and computer science graduates.",
            OfficialUrl = "https://www.isro.gov.in/Careers.html",
        },
        new()
        {
            Name = "MPESB Group 4 (Assistant Grade 3)", Authority = "MP Employee Selection Board", Level = "State",
            MinAge = 18, MaxAge = 40, QualificationRequired = "Graduate", RequiresMpDomicile = true,
            SyllabusAreas = "General knowledge|Quantitative aptitude|Computer knowledge|Hindi|English",
            Summary = "High-volume state recruitment with a computer-knowledge section that overlaps your degree directly.",
            OfficialUrl = "https://esb.mp.gov.in",
        },
        new()
        {
            Name = "MPPSC State Service Examination", Authority = "MP Public Service Commission", Level = "State",
            MinAge = 21, MaxAge = 40, QualificationRequired = "Graduate",
            SyllabusAreas = "General studies|MP-specific GK|CSAT|Essay|Interview",
            Summary = "The state's flagship administrative examination. Expect a two to three year preparation commitment.",
            OfficialUrl = "https://mppsc.mp.gov.in",
        },
        new()
        {
            Name = "MPPSC Assistant Professor", Authority = "MP Public Service Commission", Level = "State",
            MinAge = 21, MaxAge = 40, QualificationRequired = "Postgraduate with NET",
            SyllabusAreas = "Subject paper|Teaching aptitude",
            Summary = "Requires a master's degree and NET, so this becomes available only after further study.",
            OfficialUrl = "https://mppsc.mp.gov.in",
        },
    ];

    /// <summary>Upserts by name and drops rows the catalogue no longer lists (the old relative-date demo set).</summary>
    public static async Task SyncAsync(SaplingDbContext db)
    {
        var existing = await db.GovtExams.ToListAsync();
        var byName = existing.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var exam in All)
        {
            if (!byName.Remove(exam.Name, out var row))
            {
                row = new GovtExam { Name = exam.Name };
                db.GovtExams.Add(row);
            }

            row.Authority = exam.Authority;
            row.Level = exam.Level;
            row.NotificationOn = exam.NotificationOn;
            row.ExamOn = exam.ExamOn;
            row.ExamEndsOn = exam.ExamEndsOn;
            row.DatesTentative = exam.DatesTentative;
            row.MinAge = exam.MinAge;
            row.MaxAge = exam.MaxAge;
            row.QualificationRequired = exam.QualificationRequired;
            row.OpenYearsBeforeGraduation = exam.OpenYearsBeforeGraduation;
            row.RequiresMpDomicile = exam.RequiresMpDomicile;
            row.MinCgpa = exam.MinCgpa;
            row.SyllabusAreas = exam.SyllabusAreas;
            row.Summary = exam.Summary;
            row.OfficialUrl = exam.OfficialUrl;
        }

        db.GovtExams.RemoveRange(byName.Values);
        await db.SaveChangesAsync();
    }
}
