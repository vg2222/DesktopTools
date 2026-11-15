using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DesktopTools.Extras;

/// <summary>
/// Greedy against beam decoding on sentence pairs written for this repository (no third-party text). Scores are chrF (character
/// n-grams 1–6, β = 2) against one reference each; there is no pass/fail threshold, the numbers decide which decoding ships.
/// </summary>
internal static class TranslationQualityBench
{
    private static readonly (string Source, string Reference)[] EnglishRussian =
    [
        ("Save the file before you close the program.", "Сохраните файл перед тем, как закрыть программу."),
        ("The meeting starts at nine in the morning.", "Встреча начинается в девять утра."),
        ("Please send me the report by Friday.", "Пожалуйста, пришлите мне отчёт до пятницы."),
        ("The window on the left shows your recent files.", "Окно слева показывает ваши недавние файлы."),
        ("I could not find the button you described.", "Я не смог найти кнопку, которую вы описали."),
        ("The weather was cold, so we stayed at home.", "Было холодно, поэтому мы остались дома."),
        ("Click the gear icon to open the settings.", "Нажмите на значок шестерёнки, чтобы открыть настройки."),
        ("My brother works at a small bakery downtown.", "Мой брат работает в небольшой пекарне в центре города."),
        ("The update will install when you restart the computer.", "Обновление установится, когда вы перезагрузите компьютер."),
        ("We need two more chairs for the guests.", "Нам нужно ещё два стула для гостей."),
        ("The train was late because of the snow.", "Поезд опоздал из-за снега."),
        ("You can drag the picture into the editor.", "Вы можете перетащить картинку в редактор."),
        ("She reads a book every evening before bed.", "Каждый вечер перед сном она читает книгу."),
        ("This copy of the image is safe to share.", "Этой копией изображения можно безопасно поделиться."),
        ("The shop closes early on Sundays.", "По воскресеньям магазин закрывается рано."),
        ("Choose a folder where the recordings will be saved.", "Выберите папку, в которую будут сохраняться записи."),
        ("He forgot his umbrella at the station.", "Он забыл зонт на вокзале."),
        ("The printer is out of paper again.", "В принтере снова закончилась бумага."),
        ("Turn off the sound before the presentation.", "Выключите звук перед презентацией."),
        ("Our team finished the project ahead of schedule.", "Наша команда закончила проект раньше срока."),
        ("The children are playing in the garden.", "Дети играют в саду."),
        ("Check your spelling before you send the message.", "Проверьте орфографию, прежде чем отправлять сообщение."),
        ("The museum is free on the first Sunday of the month.", "В первое воскресенье месяца вход в музей бесплатный."),
        ("I will call you when I arrive.", "Я позвоню тебе, когда приеду."),
        ("The battery lasts about six hours.", "Батареи хватает примерно на шесть часов."),
        ("Write your name at the top of the page.", "Напишите своё имя вверху страницы."),
        ("The soup needs a little more salt.", "В суп нужно добавить ещё немного соли."),
        ("Press Escape to cancel the selection.", "Нажмите Escape, чтобы отменить выделение."),
        ("The road to the village is very narrow.", "Дорога в деревню очень узкая."),
        ("Do not open attachments from unknown senders.", "Не открывайте вложения от неизвестных отправителей."),
        ("My grandmother grows tomatoes and cucumbers.", "Моя бабушка выращивает помидоры и огурцы."),
        ("The video will be ready in a few minutes.", "Видео будет готово через несколько минут."),
        ("We watched a film about the history of the city.", "Мы посмотрели фильм об истории города."),
        ("Keep the receipt in case you need to return the item.", "Сохраните чек на случай, если понадобится вернуть товар."),
        ("The office is on the third floor.", "Офис находится на третьем этаже."),
        ("Copy the text and paste it into the document.", "Скопируйте текст и вставьте его в документ."),
        ("The dog sleeps next to the fireplace.", "Собака спит у камина."),
        ("Your password must contain at least eight characters.", "Пароль должен содержать не менее восьми символов."),
        ("The concert was cancelled because of the rain.", "Концерт отменили из-за дождя."),
        ("Thank you for your help with the move.", "Спасибо за помощь с переездом.")
    ];

    private static readonly (string Source, string Reference)[] RussianEnglish =
    [
        ("Закройте окно, на улице холодно.", "Close the window, it is cold outside."),
        ("Я забыл пароль от своей почты.", "I forgot the password to my email."),
        ("Магазин открывается в восемь часов.", "The shop opens at eight o'clock."),
        ("Пожалуйста, проверьте эти цифры ещё раз.", "Please check these numbers again."),
        ("Мы поедем на море в июле.", "We will go to the sea in July."),
        ("Эта программа работает без интернета.", "This program works without the internet."),
        ("Мой телефон почти разрядился.", "My phone is almost dead."),
        ("Нажмите кнопку, чтобы начать запись.", "Press the button to start recording."),
        ("Вчера весь день шёл дождь.", "It rained all day yesterday."),
        ("Где находится ближайшая аптека?", "Where is the nearest pharmacy?"),
        ("Мы купили новый стол для кухни.", "We bought a new table for the kitchen."),
        ("Файл слишком большой, чтобы отправить его по почте.", "The file is too large to send by email."),
        ("Она учится в университете на втором курсе.", "She is in her second year at university."),
        ("Выберите язык в списке справа.", "Choose a language in the list on the right."),
        ("Поезд отправляется через десять минут.", "The train leaves in ten minutes."),
        ("Я люблю гулять в парке по утрам.", "I like to walk in the park in the morning."),
        ("Сохраните изменения перед выходом.", "Save your changes before you exit."),
        ("Наш сосед починил нам велосипед.", "Our neighbour fixed our bicycle."),
        ("Компьютер работает очень медленно.", "The computer is running very slowly."),
        ("Дети нарисовали картину для мамы.", "The children drew a picture for their mother."),
        ("Встреча перенесена на следующую неделю.", "The meeting has been moved to next week."),
        ("Пожалуйста, говорите медленнее.", "Please speak more slowly."),
        ("Изображение можно обрезать перед сохранением.", "You can crop the image before saving it."),
        ("В холодильнике нет молока.", "There is no milk in the fridge."),
        ("Он каждый день ездит на работу на автобусе.", "He takes the bus to work every day."),
        ("Эта книга понравилась мне больше, чем фильм.", "I liked this book more than the film."),
        ("Отключите уведомления на время презентации.", "Turn off notifications during the presentation."),
        ("Музей закрыт по понедельникам.", "The museum is closed on Mondays."),
        ("Мы ждали автобус почти полчаса.", "We waited for the bus for almost half an hour."),
        ("Скопируйте ссылку и отправьте её коллеге.", "Copy the link and send it to a colleague."),
        ("Кошка спит на подоконнике.", "The cat is sleeping on the windowsill."),
        ("Документ был сохранён в папке загрузок.", "The document was saved in the downloads folder."),
        ("Завтра будет тепло и солнечно.", "Tomorrow will be warm and sunny."),
        ("Я не понимаю, почему программа не запускается.", "I do not understand why the program does not start."),
        ("Принесите, пожалуйста, ещё одну чашку чая.", "Please bring another cup of tea."),
        ("Видео длится всего две минуты.", "The video is only two minutes long."),
        ("Мой брат живёт в другом городе.", "My brother lives in another city."),
        ("Удалите старые файлы, чтобы освободить место.", "Delete old files to free up space."),
        ("Урок начинается ровно в полдень.", "The lesson starts exactly at noon."),
        ("Спасибо, что пришли так быстро.", "Thank you for coming so quickly.")
    ];

    public static async Task RunAsync()
    {
        // One beam must reproduce the greedy output this release has always produced.
        LocalTranslation.BeamsOverride = 1;
        try
        {
            string baseline = await LocalTranslation.TranslateAsync("Hello world! Open the window. The computer is on the table.", "en-ru");
            if (baseline != "Приветствую мир! Открой окно. Компьютер на столе.") throw new Exception("One beam changed the greedy output: " + baseline);
        }
        finally { LocalTranslation.BeamsOverride = null; }
        Console.WriteLine("direction  beams  chrF   seconds");
        foreach (var (direction, pairs) in new[] { ("en-ru", EnglishRussian), ("ru-en", RussianEnglish) })
        {
            if (pairs.Length != 40) throw new Exception(direction + " needs 40 pairs");
            foreach (int beams in new[] { 1, 4 })
            {
                LocalTranslation.BeamsOverride = beams;
                try
                {
                    await LocalTranslation.TranslateAsync(pairs[0].Source, direction); // load the sessions outside the timing
                    var watch = Stopwatch.StartNew();
                    var hypotheses = new List<string>();
                    foreach (var pair in pairs) hypotheses.Add(await LocalTranslation.TranslateAsync(pair.Source, direction));
                    double seconds = watch.Elapsed.TotalSeconds;
                    double score = ChrF(hypotheses, pairs.Select(p => p.Reference).ToList());
                    Console.WriteLine($"{direction,-9}  {beams,5}  {score,5:F1}  {seconds,7:F2}");
                }
                finally { LocalTranslation.BeamsOverride = null; }
            }
        }
    }

    /// <summary>Corpus chrF: character n-gram precision and recall (n = 1..6, spaces removed) averaged over n, combined with β = 2.</summary>
    internal static double ChrF(IReadOnlyList<string> hypotheses, IReadOnlyList<string> references)
    {
        double precisionSum = 0, recallSum = 0; int orders = 0;
        for (int n = 1; n <= 6; n++)
        {
            long match = 0, hypothesisTotal = 0, referenceTotal = 0;
            for (int i = 0; i < hypotheses.Count; i++)
            {
                var h = Grams(hypotheses[i], n); var r = Grams(references[i], n);
                hypothesisTotal += h.Values.Sum(); referenceTotal += r.Values.Sum();
                foreach (var (gram, count) in h) if (r.TryGetValue(gram, out int other)) match += Math.Min(count, other);
            }
            if (hypothesisTotal == 0 || referenceTotal == 0) continue;
            precisionSum += (double)match / hypothesisTotal; recallSum += (double)match / referenceTotal; orders++;
        }
        if (orders == 0) return 0;
        double precision = precisionSum / orders, recall = recallSum / orders;
        const double beta2 = 4;
        return precision + recall == 0 ? 0 : 100 * (1 + beta2) * precision * recall / (beta2 * precision + recall);
    }

    private static Dictionary<string, int> Grams(string text, int n)
    {
        string compact = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var grams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i + n <= compact.Length; i++) { string gram = compact.Substring(i, n); grams[gram] = grams.GetValueOrDefault(gram) + 1; }
        return grams;
    }
}
