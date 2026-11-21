using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Training;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Training;
using MerfitCustomerApi.Business.Services.WorkoutSessions;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Training;

/// <summary>
/// Program servisinin versiyonlama ve gecmis koruma kurallari; antrenman oturumunun motor gunleriyle
/// (katalog antrenmani olmayan plan gunu) calismasi, set kaydi, geri bildirim ve progresyon.
/// </summary>
public class TrainingProgramServiceTests
{
    private const long UserId = 7;

    private sealed class Fixture
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public TrainingEngineOptions Options { get; } = new();
        public TrainingProgramService Programs { get; }
        public WorkoutSessionService Sessions { get; }

        public Fixture(ExperienceLevel experience = ExperienceLevel.Intermediate, int days = 4)
        {
            TrainingTestCatalog.Seed(Uow);
            foreach (var register in new Action[]
            {
                () => Uow.Register<UserTrainingPreference>(), () => Uow.Register<UserGoal>(), () => Uow.Register<UserEquipment>(),
                () => Uow.Register<WorkoutPlan>(), () => Uow.Register<WorkoutPlanDay>(), () => Uow.Register<WorkoutPlanExercise>(),
                () => Uow.Register<WorkoutSession>(), () => Uow.Register<WorkoutSessionExercise>(), () => Uow.Register<WorkoutSetLog>(),
                () => Uow.Register<WorkoutSessionFeedback>(), () => Uow.Register<Workout>(), () => Uow.Register<PersonalRecord>(),
                () => Uow.Register<UserStreak>(), () => Uow.Register<BodyMeasurement>(),
            })
            {
                register();
            }

            Uow.Set<UserProfile>().Add(new UserProfile
            {
                Id = 1, UserId = UserId, FirstName = "T", LastName = "U", Username = "tu",
                ExperienceLevel = experience, TrainingDaysPerWeek = days, WorkoutDurationMin = 60,
                TrainingLocation = TrainingLocation.Gym, Goal = FitnessGoal.BuildMuscle,
            });

            var options = Microsoft.Extensions.Options.Options.Create(Options);
            Programs = new TrainingProgramService(Uow.Object, options);
            Sessions = new WorkoutSessionService(Uow.Object, Mock.Of<IMerfitScoreService>(), engineOptions: options);
        }

        public List<WorkoutPlan> Plans => Uow.Set<WorkoutPlan>();

        public async Task<long> CompleteDayAsync(TrainingDayDto day, Func<TrainingExerciseDto, (decimal Weight, int Reps, int? Rir)> perform)
        {
            var session = await Sessions.StartSessionAsync(UserId, new StartWorkoutSessionRequest { WorkoutPlanDayId = day.WorkoutPlanDayId });
            foreach (var exercise in session.Exercises)
            {
                var planned = day.Exercises.Single(e => e.ExerciseId == exercise.ExerciseId);
                var (weight, reps, rir) = perform(planned);
                for (var set = 1; set <= planned.Sets; set++)
                {
                    await Sessions.LogSetAsync(UserId, session.Id, exercise.SessionExerciseId, new LogWorkoutSetRequest { SetNumber = set, WeightKg = weight, Reps = reps, Rir = rir });
                }
            }

            await Sessions.CompleteSessionAsync(UserId, session.Id, new CompleteWorkoutSessionRequest { DurationSeconds = 3600 });

            // Gecmis tarihli gibi davran: bir sonraki oturum bu oturumu "onceki performans" olarak gorur.
            var entity = Uow.Set<WorkoutSession>().Single(s => s.Id == session.Id);
            entity.StartedAt = entity.StartedAt.AddDays(-3);
            entity.CompletedAt = entity.StartedAt.AddHours(1);
            return session.Id;
        }
    }

    private static GenerateTrainingProgramRequest Request(int days = 4, string focus = "Hypertrophy") => new() { Focus = focus, DaysPerWeek = days, SessionMinutes = 60 };

    [Fact]
    public async Task Generate_FirstProgram_IsActiveV1_FromCatalogOnly_WithoutWorkoutTemplates()
    {
        var f = new Fixture();
        var program = await f.Programs.GenerateAsync(UserId, Request());

        Assert.Equal(1, program.Version);
        Assert.Equal("Active", program.Status);
        Assert.Equal("UpperLower", program.SplitType);
        Assert.Equal(4, program.Days.Count);
        Assert.All(f.Uow.Set<WorkoutPlanDay>(), d => Assert.Null(d.WorkoutId));
        var exerciseIds = TrainingTestCatalog.Ids.ToHashSet();
        Assert.All(program.Days.SelectMany(d => d.Exercises), e => Assert.Contains(e.ExerciseId, exerciseIds));
        Assert.All(program.Days.SelectMany(d => d.Exercises), e => Assert.NotNull(e.TargetRir));
        Assert.All(program.Days.SelectMany(d => d.Exercises), e => Assert.False(string.IsNullOrWhiteSpace(e.WeightInstruction)));
        // Hic egzersiz eklenmedi / cogaltilmadi.
        Assert.Equal(TrainingTestCatalog.Count, f.Uow.Set<Exercise>().Count);
    }

    [Fact]
    public async Task Regenerate_WhileActive_CreatesProposal_AndDoesNotTouchActiveProgram()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var v1Exercises = f.Uow.Set<WorkoutPlanExercise>().Select(e => (e.Id, e.ExerciseId, e.Sets)).ToList();

        var v2 = await f.Programs.GenerateAsync(UserId, Request(3));

        Assert.Equal("Proposed", v2.Status);
        Assert.Equal(2, v2.Version);
        Assert.Equal(v1.Id, v2.PreviousPlanId);
        Assert.Contains(v2.Changes, c => c.Type == "split");
        var active = await f.Programs.GetActiveAsync(UserId);
        Assert.Equal(v1.Id, active!.Id);
        Assert.Equal(v1Exercises, f.Uow.Set<WorkoutPlanExercise>().Where(e => v1Exercises.Select(x => x.Id).Contains(e.Id)).Select(e => (e.Id, e.ExerciseId, e.Sets)).ToList());
    }

    [Fact]
    public async Task Accept_ArchivesOldVersion_AndKeepsCompletedHistoryUnchanged()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var sessionId = await f.CompleteDayAsync(v1.Days[0], e => (40m, e.RepsMax ?? 10, 2));
        var logsBefore = f.Uow.Set<WorkoutSetLog>().Select(l => (l.Id, l.WeightKg, l.Reps, l.Rir)).ToList();
        var sessionDayId = f.Uow.Set<WorkoutSession>().Single(s => s.Id == sessionId).WorkoutPlanDayId;

        var v2 = await f.Programs.GenerateAsync(UserId, Request(3));
        var accepted = await f.Programs.AcceptProposalAsync(UserId, v2.Id);

        Assert.Equal("Active", accepted.Status);
        var old = f.Plans.Single(p => p.Id == v1.Id);
        Assert.Equal(TrainingProgramStatus.Archived, old.Status);
        Assert.False(old.IsActive);
        Assert.NotNull(old.ArchivedAt);
        Assert.Single(f.Plans, p => p.IsActive);

        // Eski plan gunleri ve tamamlanan oturum / setler degismedi.
        Assert.Contains(f.Uow.Set<WorkoutPlanDay>(), d => d.Id == sessionDayId && d.WorkoutPlanId == v1.Id);
        Assert.Equal(sessionDayId, f.Uow.Set<WorkoutSession>().Single(s => s.Id == sessionId).WorkoutPlanDayId);
        Assert.Equal(logsBefore, f.Uow.Set<WorkoutSetLog>().Select(l => (l.Id, l.WeightKg, l.Reps, l.Rir)).ToList());

        var versions = await f.Programs.GetVersionsAsync(UserId);
        Assert.Equal(new[] { 2, 1 }, versions.Select(v => v.Version));
        Assert.Equal(1, versions.Single(v => v.Version == 1).CompletedSessions);
    }

    [Fact]
    public async Task Reject_KeepsActive_AndStaleProposalCannotBeAccepted()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var p2 = await f.Programs.GenerateAsync(UserId, Request(3));
        await f.Programs.RejectProposalAsync(UserId, p2.Id);
        Assert.Equal(TrainingProgramStatus.Rejected, f.Plans.Single(p => p.Id == p2.Id).Status);
        Assert.Equal(v1.Id, (await f.Programs.GetActiveAsync(UserId))!.Id);

        // Yeni oneri olusur; ardindan kullanici elle degisiklik yapar (yeni aktif versiyon). Eski oneri artik gecersiz.
        var p3 = await f.Programs.ProposeUpdateAsync(UserId);
        var exercise = v1.Days[0].Exercises[0];
        var alternative = (await f.Programs.GetAlternativesAsync(UserId, exercise.PlanExerciseId)).First();
        await f.Programs.SwapExerciseAsync(UserId, exercise.PlanExerciseId, new SwapExerciseRequest { NewExerciseId = alternative.ExerciseId });

        await Assert.ThrowsAsync<ConflictException>(() => f.Programs.AcceptProposalAsync(UserId, p3.Id));
    }

    [Fact]
    public async Task ManualSwap_CreatesManualVersion_WithAlternativeFromCatalog_AndArchivesPrevious()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var target = v1.Days[0].Exercises[0];
        var alternatives = await f.Programs.GetAlternativesAsync(UserId, target.PlanExerciseId);
        Assert.NotEmpty(alternatives);
        Assert.All(alternatives, a => Assert.Contains(a.ExerciseId, TrainingTestCatalog.Ids));

        var swapped = await f.Programs.SwapExerciseAsync(UserId, target.PlanExerciseId, new SwapExerciseRequest { NewExerciseId = alternatives[0].ExerciseId });

        Assert.Equal("Manual", swapped.Source);
        Assert.Equal(2, swapped.Version);
        Assert.Equal(alternatives[0].ExerciseId, swapped.Days[0].Exercises.Single(e => e.Order == target.Order).ExerciseId);
        Assert.Equal(TrainingProgramStatus.Archived, f.Plans.Single(p => p.Id == v1.Id).Status);
        // Eski versiyonun egzersizi yerinde duruyor.
        Assert.Contains(f.Uow.Set<WorkoutPlanExercise>(), e => e.Id == target.PlanExerciseId && e.ExerciseId == target.ExerciseId);
        Assert.Contains(swapped.Changes, c => c.Type == "exercise_swapped");
    }

    [Fact]
    public async Task Swap_ToExerciseOutsideCatalogOrAvoided_IsRejected()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var target = v1.Days[0].Exercises[0];

        await Assert.ThrowsAnyAsync<Exception>(() => f.Programs.SwapExerciseAsync(UserId, target.PlanExerciseId, new SwapExerciseRequest { NewExerciseId = 9999 }));
    }

    [Fact]
    public async Task EngineDay_Session_LogsRir_SameSetTwiceUpdates_AndNextSessionProgresses()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var day = v1.Days[0];

        var session = await f.Sessions.StartSessionAsync(UserId, new StartWorkoutSessionRequest { WorkoutPlanDayId = day.WorkoutPlanDayId });
        Assert.Null(session.WorkoutId);
        Assert.Equal(day.Title, session.Title);
        var first = session.Exercises[0];
        Assert.NotNull(first.TargetRir);
        Assert.Equal("calibrate", first.Progression!.Action);

        // Ayni set iki kez gonderilir (tekrar / cift dokunma): tek kayit, son degerler.
        await f.Sessions.LogSetAsync(UserId, session.Id, first.SessionExerciseId, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = 40m, Reps = 8, Rir = 3 });
        await f.Sessions.LogSetAsync(UserId, session.Id, first.SessionExerciseId, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = 42.5m, Reps = 9, Rir = 2 });
        var logs = f.Uow.Set<WorkoutSetLog>().Where(l => l.WorkoutSessionExerciseId == first.SessionExerciseId).ToList();
        var log = Assert.Single(logs);
        Assert.Equal((42.5m, 9, 2), (log.WeightKg!.Value, log.Reps!.Value, log.Rir!.Value));

        // RPE verilirse RIR = 10 - RPE.
        await f.Sessions.LogSetAsync(UserId, session.Id, first.SessionExerciseId, new LogWorkoutSetRequest { SetNumber = 2, WeightKg = 42.5m, Reps = 9, Rpe = 8m });
        Assert.Equal(2, f.Uow.Set<WorkoutSetLog>().Single(l => l.WorkoutSessionExerciseId == first.SessionExerciseId && l.SetNumber == 2).Rir);
        await f.Sessions.CancelSessionAsync(UserId, session.Id);

        // Tum setlerde ust sinira hedef RIR ile ulasildi -> sonraki oturumda kucuk artis.
        await f.CompleteDayAsync(day, e => (40m, e.RepsMax ?? 10, e.TargetRir ?? 2));
        var next = await f.Sessions.StartSessionAsync(UserId, new StartWorkoutSessionRequest { WorkoutPlanDayId = day.WorkoutPlanDayId });
        var weighted = next.Exercises.Where(e => e.Progression?.PreviousWeightKg is not null).ToList();
        Assert.NotEmpty(weighted);
        Assert.All(weighted, e =>
        {
            Assert.Equal("increase_weight", e.Progression!.Action);
            Assert.True(e.Progression.SuggestedWeightKg - 40m is > 0 and <= 5m);
        });
    }

    [Fact]
    public async Task Feedback_WithDiscomfort_ReturnsSafetyText_AndNextProposalReplacesExercise()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var day = v1.Days[0];
        var sessionId = await f.CompleteDayAsync(day, e => (40m, e.RepsMin ?? 8, 2));
        var painful = day.Exercises[0].ExerciseId;

        var feedback = await f.Sessions.SubmitFeedbackAsync(UserId, sessionId, new SubmitWorkoutFeedbackRequest
        {
            Difficulty = 3, Fatigue = 3, Recovery = 3, Satisfaction = 4, HasDiscomfort = true, DiscomfortExerciseIds = new List<long> { painful },
        });
        Assert.False(string.IsNullOrWhiteSpace(feedback.SafetyMessage));

        // Ayni oturum icin tekrar gonderim gunceller (tek kayit).
        await f.Sessions.SubmitFeedbackAsync(UserId, sessionId, new SubmitWorkoutFeedbackRequest { Difficulty = 4, HasDiscomfort = true, DiscomfortExerciseIds = new List<long> { painful } });
        Assert.Single(f.Uow.Set<WorkoutSessionFeedback>());

        var proposal = await f.Programs.ProposeUpdateAsync(UserId);
        Assert.DoesNotContain(proposal.Days.SelectMany(d => d.Exercises), e => e.ExerciseId == painful);
        Assert.Equal("Proposed", proposal.Status);
        Assert.Equal(v1.Id, (await f.Programs.GetActiveAsync(UserId))!.Id);
    }

    [Fact]
    public async Task Feedback_OnUnfinishedSession_IsRejected_AndForeignExerciseIsRejected()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var started = await f.Sessions.StartSessionAsync(UserId, new StartWorkoutSessionRequest { WorkoutPlanDayId = v1.Days[0].WorkoutPlanDayId });
        await Assert.ThrowsAnyAsync<AppValidationException>(() => f.Sessions.SubmitFeedbackAsync(UserId, started.Id, new SubmitWorkoutFeedbackRequest { Difficulty = 3 }));
        await f.Sessions.CancelSessionAsync(UserId, started.Id);

        var sessionId = await f.CompleteDayAsync(v1.Days[0], e => (40m, 8, 2));
        await Assert.ThrowsAnyAsync<AppValidationException>(() => f.Sessions.SubmitFeedbackAsync(UserId, sessionId,
            new SubmitWorkoutFeedbackRequest { HasDiscomfort = true, DiscomfortExerciseIds = new List<long> { 9999 } }));
    }

    [Fact]
    public async Task ProposedProgramDay_CannotBeStarted()
    {
        var f = new Fixture();
        await f.Programs.GenerateAsync(UserId, Request(4));
        var proposal = await f.Programs.GenerateAsync(UserId, Request(3));
        await Assert.ThrowsAnyAsync<AppValidationException>(() =>
            f.Sessions.StartSessionAsync(UserId, new StartWorkoutSessionRequest { WorkoutPlanDayId = proposal.Days[0].WorkoutPlanDayId }));
    }

    [Fact]
    public async Task Progress_And_ExerciseHistory_ComeFromRealSets()
    {
        var f = new Fixture();
        var v1 = await f.Programs.GenerateAsync(UserId, Request(4));
        var empty = await f.Programs.GetProgressAsync(UserId);
        Assert.Empty(empty.Insights);
        Assert.Equal(0, empty.CompletedSessions4Weeks);

        await f.CompleteDayAsync(v1.Days[0], e => (40m, e.RepsMax ?? 10, 2));
        var progress = await f.Programs.GetProgressAsync(UserId);
        Assert.Equal(1, progress.CompletedSessions4Weeks);
        var exerciseId = v1.Days[0].Exercises.First(e => e.SuggestedWeightKg is null && e.WeightGuidance == "RampUp").ExerciseId;
        var history = await f.Programs.GetExerciseHistoryAsync(UserId, exerciseId);
        Assert.NotNull(history);
        Assert.Single(history!.Points);
    }

    [Fact]
    public async Task CatalogPlanUser_GeneratingEngineProgram_ArchivesCatalogPlan_AndKeepsIt()
    {
        var f = new Fixture();
        f.Plans.Add(new WorkoutPlan { Id = 500, UserId = UserId, Name = "Hazır plan", IsActive = true, StartDate = DateTime.UtcNow.AddDays(-30), Status = TrainingProgramStatus.Active, Source = TrainingProgramSource.Catalog });

        var v1 = await f.Programs.GenerateAsync(UserId, Request(3));

        Assert.Equal("Active", v1.Status);
        var catalogPlan = f.Plans.Single(p => p.Id == 500);
        Assert.Equal(TrainingProgramStatus.Archived, catalogPlan.Status);
        Assert.Contains(v1.Changes, c => c.Type == "note");
    }

    [Fact]
    public async Task SelectedEquipment_IsStoredInExistingTable_AndLimitsSelection()
    {
        var f = new Fixture();
        var request = Request(3);
        request.EquipmentIds = new List<long> { TrainingTestCatalog.Dumbbell };
        var program = await f.Programs.GenerateAsync(UserId, request);

        Assert.Single(f.Uow.Set<UserEquipment>(), e => e.UserId == UserId && e.EquipmentId == TrainingTestCatalog.Dumbbell);
        var prefs = await f.Programs.GetPreferencesAsync(UserId);
        Assert.Equal("selected", prefs.EquipmentMode);
        var dumbbellOrBodyweight = new[] { "Dumbbell", "Bodyweight" };
        var names = program.Days.SelectMany(d => d.Exercises).Select(e => e.Name).ToList();
        Assert.DoesNotContain(names, n => n.StartsWith("Barbell", StringComparison.Ordinal) || n.Contains("Machine", StringComparison.Ordinal) || n.StartsWith("Cable", StringComparison.Ordinal));
        Assert.All(program.Days.SelectMany(d => d.Exercises), e =>
            Assert.Contains(f.Uow.Set<Exercise>().Single(x => x.Id == e.ExerciseId).EquipmentType, dumbbellOrBodyweight));

        // Secim temizlenince salon profili yeniden tum ekipmanlara doner.
        await f.Programs.UpdatePreferencesAsync(UserId, new TrainingPreferencesDto { EquipmentIds = new List<long>() });
        Assert.Equal("full", (await f.Programs.GetPreferencesAsync(UserId)).EquipmentMode);
    }

    [Fact]
    public async Task Preferences_RejectUnknownOrConflictingIds()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<AppValidationException>(() => f.Programs.UpdatePreferencesAsync(UserId, new TrainingPreferencesDto { PreferredExerciseIds = new List<long> { 9999 } }));
        await Assert.ThrowsAsync<AppValidationException>(() => f.Programs.UpdatePreferencesAsync(UserId, new TrainingPreferencesDto { PreferredExerciseIds = new List<long> { 1 }, AvoidedExerciseIds = new List<long> { 1 } }));
        await Assert.ThrowsAsync<AppValidationException>(() => f.Programs.UpdatePreferencesAsync(UserId, new TrainingPreferencesDto { EquipmentIds = new List<long> { 77 } }));
    }

    private static TrainingCoachService Coach(Fixture f, string? reply, bool throws = false)
    {
        var model = new Mock<INutritionAssistantModelClient>();
        model.SetupGet(m => m.IsConfigured).Returns(reply is not null || throws);
        model.SetupGet(m => m.Model).Returns("test");
        var setup = model.Setup(m => m.CompleteAsync(It.IsAny<NutritionAssistantModelRequest>(), It.IsAny<CancellationToken>()));
        if (throws)
        {
            setup.ThrowsAsync(new ExternalServiceUnavailableException("down"));
        }
        else
        {
            setup.ReturnsAsync(new NutritionAssistantModelResult(reply ?? string.Empty, 1, 1, "end", "test", false));
        }

        return new TrainingCoachService(f.Uow.Object, f.Programs, Microsoft.Extensions.Options.Options.Create(f.Options), model.Object);
    }

    [Fact]
    public async Task Coach_ValidatedAiAnswer_IsReturned_InventedExerciseFallsBackToRules()
    {
        var f = new Fixture();
        var program = await f.Programs.GenerateAsync(UserId, Request(4));
        var inProgram = program.Days[0].Exercises[0];

        var ok = await Coach(f, $$"""{"message":"{{inProgram.Name}} ana hareketin; tekniğe odaklan.","exerciseIds":[{{inProgram.ExerciseId}}]}""")
            .AskAsync(UserId, new TrainingCoachRequest { Topic = "program" });
        Assert.Equal("ai", ok.Source);
        Assert.Equal(new[] { inProgram.ExerciseId }, ok.ReferencedExerciseIds);

        var invented = await Coach(f, """{"message":"Bulgarian Split Squat ekle.","exerciseIds":[424242]}""")
            .AskAsync(UserId, new TrainingCoachRequest { Topic = "program" });
        Assert.Equal("rules", invented.Source);
        Assert.False(string.IsNullOrWhiteSpace(invented.Message));
        Assert.NotNull(invented.ValidationNote);

        var down = await Coach(f, null, throws: true).AskAsync(UserId, new TrainingCoachRequest { Topic = "progress" });
        Assert.Equal("rules", down.Source);

        var notConfigured = await Coach(f, null).AskAsync(UserId, new TrainingCoachRequest { Topic = "recovery" });
        Assert.Equal("rules", notConfigured.Source);
    }
}
