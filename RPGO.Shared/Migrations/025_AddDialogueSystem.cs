using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(25)]
public class AddDialogueSystem : ForwardOnlyMigration
{
    private const string ElderDialogue = @"{
  ""greeting"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Приветствую, путник. Наше деревне нужна помощь."",
    ""choices"": [
      { ""text"": ""Что случилось?"", ""next"": ""story1"", ""condition"": ""quest_not_active:Q0007"" },
      { ""text"": ""Как идёт охота на волков?"", ""next"": ""quest_progress"", ""condition"": ""quest_active:Q0007"" },
      { ""text"": ""Волки побеждены. Все пятеро мертвы."", ""next"": ""quest_turnin"", ""condition"": ""quest_ready:Q0007"" },
      { ""text"": ""У меня есть задание от торговца."", ""next"": ""merchant_quest"", ""condition"": ""quest_ready:Q0001"" },
      { ""text"": ""Простите, мне пора."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_progress"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Ты ещё не вернулся с трофеями. Стая всё ещё бродит у околицы. Будь осторожен и возвращайся, когда справишься со всеми пятью."",
    ""choices"": [
      { ""text"": ""Постараюсь быстрее."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story1"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""На прошлой неделе охотник Тихон вышел в лес и не вернуся. Мы нашли его лук сломанным у развилки тропинок. А сегодня утром стая волков вышла к самой околице."",
    ""choices"": [
      { ""text"": ""Волки? Разве это серьёзная угроза?"", ""next"": ""story2"" },
      { ""text"": ""Мне жаль охотника, но у меня свои дела."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story2"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Серьёзная. Пять зверей — и не простых, а голодных. Они уже покалечили козу у ближайшего двора. Если стая наберётся смелости — нападут на людей. Дети боятся выходить во двор играть."",
    ""choices"": [
      { ""text"": ""Я помогу. Отправлюсь на охоту на волков."", ""next"": ""story_accept"", ""action"": ""accept_quest:Q0007"", ""condition"": ""quest_not_active:Q0007"" },
      { ""text"": ""Я уже охотюсь на них."", ""next"": null, ""condition"": ""quest_active:Q0007"", ""action"": ""close"" },
      { ""text"": ""Пять волков — это серьёзно. Мне нужно подготовиться."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story_accept"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Благодарю! Будь осторожен — волки хитрые звери. Они держатся вместе и нападают стаей. Убей всех пятерых и возвращайся — я награжу тебя по заслугам."",
    ""choices"": [
      { ""text"": ""Вернусь с победой."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_turnin"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Неужели правда? Все пятеро?! Ты настоящий воин! Деревня будет помнить твой подвиг. Прими мою благодарность — и вот, возьми это. Ты заслужил."",
    ""choices"": [
      { ""text"": ""Спасибо, староста!"", ""next"": null, ""action"": ""complete_quest:Q0007"" }
    ]
  },
  ""merchant_quest"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""А, ты помогаешь торговцу? Передай ему мой привет. Ты заслужил награду!"",
    ""choices"": [
      { ""text"": ""Спасибо!"", ""next"": null, ""action"": ""complete_quest:Q0001"" }
    ]
  }
}";

    private const string MerchantDialogue = @"{
  ""greeting"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Добро пожаловать в мой магазин! Чем могу помочь?"",
    ""choices"": [
      { ""text"": ""Покажи товары."", ""next"": null, ""action"": ""open_shop"" },
      { ""text"": ""Есть работа?"", ""next"": ""quest_offer"", ""condition"": ""quest_not_active:Q0001"" },
      { ""text"": ""Я выполнил задание."", ""next"": ""quest_turnin"", ""condition"": ""quest_ready:Q0001"" },
      { ""text"": ""Я ещё собираю хвосты."", ""next"": null, ""condition"": ""quest_active:Q0001"", ""action"": ""close"" },
      { ""text"": ""До свидания."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_offer"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Крысы завелись в моём подвале. Достань мне 5 крысиных хвостов — хорошо заплачу."",
    ""choices"": [
      { ""text"": ""Беру задание!"", ""next"": ""quest_accept"", ""action"": ""accept_quest:Q0001"" },
      { ""text"": ""Не сейчас."", ""next"": ""greeting"" }
    ]
  },
  ""quest_accept"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Отлично! Крысы водятся рядом со мной. Принеси мне 5 хвостов."",
    ""choices"": [
      { ""text"": ""До встречи."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_turnin"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Отлично, вот твоя награда! Ты настоящий герой."",
    ""choices"": [
      { ""text"": ""Спасибо!"", ""next"": null, ""action"": ""complete_quest:Q0001"" }
    ]
  }
}";

    public override void Up()
    {
        Execute.Sql("ALTER TABLE quests_def ADD COLUMN target_npc_id TEXT DEFAULT ''");

        Execute.Sql("DELETE FROM npcs WHERE id = 'N0003'");
        Insert.IntoTable("npcs").Row(new { id = "N0003", name = "Староста", type = "npc", x = 48, y = 52, data = (string?)ElderDialogue });

        Execute.Sql("DELETE FROM npcs WHERE id = 'N0001'");
        Insert.IntoTable("npcs").Row(new { id = "N0001", name = "Торговец", type = "merchant", x = 50, y = 50, data = (string?)MerchantDialogue });

        Execute.Sql("INSERT OR IGNORE INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward) VALUES ('Q0004', 'Змеиная угроза', 'Поговори со старостой и убей 3 змеи.', 'kill', 'M0013', '', 'N0003', 3, 40, 25)");
        Execute.Sql("INSERT OR IGNORE INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward) VALUES ('Q0007', 'Волчья стая', 'Перед старостой стая волков угрожает деревне. Убей 5 волков.', 'kill', 'M0006', '', 'N0003', 5, 80, 50)");
    }
}
