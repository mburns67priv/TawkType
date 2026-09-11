using Talk2Me.Core.Settings;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

public class DictationModeTests
{
    private static readonly DictationMode[] Modes = DictationModes.BuiltIn();

    [Fact]
    public void The_four_modes_ship_with_distinct_names()
        => Assert.Equal(4, Modes.Select(m => m.Name).Distinct().Count());

    /// <summary>
    /// A mode naming itself is the whole point; a settings file that names one since deleted still has
    /// to dictate.
    /// </summary>
    [Fact]
    public void An_unknown_mode_name_resolves_to_something_usable()
    {
        Assert.Equal("Chat", DictationModes.Resolve(Modes, "chat").Name);
        Assert.Equal(DictationModes.CleanProse, DictationModes.Resolve(Modes, "Deleted").Name);
        Assert.NotNull(DictationModes.Resolve([], "anything"));
    }

    [Fact]
    public void One_key_cycles_round_and_wraps()
    {
        var names = Modes.Select(m => m.Name).ToArray();

        Assert.Equal(names[1], DictationModes.Next(Modes, names[0]).Name);
        Assert.Equal(names[0], DictationModes.Next(Modes, names[^1]).Name);
        Assert.Equal(names[0], DictationModes.Next(Modes, "gone").Name);
    }

    // ----- what each mode actually does, locally -----

    [Fact]
    public void Literal_keeps_what_the_recogniser_produced()
    {
        var literal = Modes.First(m => m.Name == "Literal");

        Assert.Equal("um the deadline is tuesday", BasicTextCleaner.Clean("um the deadline is tuesday", literal));
    }

    [Fact]
    public void Clean_prose_tidies_and_capitalises()
    {
        var prose = DictationModes.Resolve(Modes, DictationModes.CleanProse);

        Assert.Equal("The deadline is Tuesday.", BasicTextCleaner.Clean("um the deadline is Tuesday.", prose));
    }

    [Fact]
    public void Chat_removes_fillers_without_forcing_a_capital()
    {
        var chat = Modes.First(m => m.Name == "Chat");

        Assert.Equal("sounds good to me", BasicTextCleaner.Clean("um sounds good to me", chat));
    }

    /// <summary>
    /// "Literal" means keeping what the recogniser gave us. It is not a promise that everything said
    /// was recovered, and no mode can make it one.
    /// </summary>
    [Fact]
    public void Literal_is_the_only_mode_that_never_reaches_a_model()
    {
        Assert.False(Modes.First(m => m.Name == "Literal").MayUseLlm);
        Assert.All(Modes.Where(m => m.Name != "Literal"), m => Assert.True(m.MayUseLlm));
    }

    /// <summary>
    /// Nobody says "um" in block capitals. This is what "preserve acronyms" amounts to without a
    /// model, and it is a rule rather than a judgement, so it is right every time.
    /// </summary>
    [Theory]
    [InlineData("the ER is open until nine", "The ER is open until nine")]
    [InlineData("I work in AH and he works in ER", "I work in AH and he works in ER")]
    public void An_all_capitals_word_is_not_a_filler(string raw, string expected)
        => Assert.Equal(expected, BasicTextCleaner.Clean(raw, new DictationMode()));

    [Fact]
    public void A_lower_case_filler_is_still_removed()
        => Assert.Equal("The deadline is Tuesday", BasicTextCleaner.Clean("um the deadline is er Tuesday", new DictationMode()));

    /// <summary>A capital at the start of a sentence is not evidence of an acronym.</summary>
    [Fact]
    public void A_sentence_leading_filler_is_still_removed()
        => Assert.Equal("The deadline is Tuesday", BasicTextCleaner.Clean("Um, the deadline is Tuesday", new DictationMode()));

    // ----- vocabulary layering -----

    [Fact]
    public void A_mode_adds_its_words_to_the_main_list_rather_than_replacing_it()
    {
        var main = new VocabularySettings { Replacements = [new TextReplacement("jupitor", "Jupitor Studio")] };
        var mode = new VocabularySettings { Replacements = [new TextReplacement("get user", "getUser")] };

        var combined = main.With(mode);

        Assert.Equal(2, combined.Replacements.Length);
        Assert.Equal("getUser", PhraseBook.Apply("get user", combined).Text);
        Assert.Equal("Jupitor Studio", PhraseBook.Apply("jupitor", combined).Text);
    }

    /// <summary>
    /// Where both name the same phrase the mode's entry wins, so a technical mode can say what "the
    /// client" means without the user deleting their general entry for it.
    /// </summary>
    [Fact]
    public void The_modes_own_entry_wins_where_both_name_a_phrase()
    {
        var main = new VocabularySettings { Replacements = [new TextReplacement("the client", "the customer")] };
        var mode = new VocabularySettings { Replacements = [new TextReplacement("the client", "the HTTP client")] };

        Assert.Equal("the HTTP client", PhraseBook.Apply("the client", main.With(mode)).Text);
    }

    [Fact]
    public void An_empty_mode_vocabulary_changes_nothing()
    {
        var main = new VocabularySettings { Spellings = ["Talk2Me"] };

        Assert.Same(main, main.With(new VocabularySettings()));
        Assert.Same(main, main.With(null));
    }

    /// <summary>
    /// Someone who had turned the trailing space off must not find it back on because this version
    /// moved where that answer lives.
    /// </summary>
    [Fact]
    public void An_older_settings_file_hands_its_answers_to_every_mode()
    {
        var older = new Talk2MeSettings
        {
            Modes = [],
            AppendTrailingSpace = false,
            RemoveFillerWords = false,
            FitToCaret = false,
        };

        var migrated = SettingsStore.Migrate(older);

        Assert.NotEmpty(migrated.Modes);
        Assert.All(migrated.Modes, m => Assert.False(m.AppendTrailingSpace));
        Assert.All(migrated.Modes, m => Assert.False(m.RemoveFillerWords));
        Assert.All(migrated.Modes, m => Assert.False(m.FitToCaret));
    }

    /// <summary>Migration must not switch anything on that the mode itself says is off.</summary>
    [Fact]
    public void Migration_never_turns_a_modes_own_choice_back_on()
    {
        var older = new Talk2MeSettings { Modes = [], AppendTrailingSpace = true, RemoveFillerWords = true };

        var chat = SettingsStore.Migrate(older).Modes.First(m => m.Name == "Chat");

        Assert.False(chat.AppendTrailingSpace);
        Assert.True(chat.RemoveFillerWords);
    }

    [Fact]
    public void Modes_are_deep_copied_with_the_settings()
    {
        var settings = new Talk2MeSettings();
        var copy = settings.Clone();

        copy.Modes[0].Name = "Changed";

        Assert.NotEqual("Changed", settings.Modes[0].Name);
    }
}
