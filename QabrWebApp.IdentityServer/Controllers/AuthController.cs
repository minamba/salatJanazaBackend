using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using MimeKit.Utils;
using QabrWebApp.IdentityServer.Models;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.IdentityServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        private static readonly ConcurrentDictionary<string, (string Code, string Token, DateTime Expiry)> _resetCodes = new();

        private sealed record ResetL10n(string Subject, string Subtitle, string Title, string Body, string Footer, bool Rtl = false);

        private static readonly Dictionary<string, ResetL10n> _resetStrings = new()
        {
            ["fr"] = new("Réinitialisation de votre mot de passe — Salat Janaza", "Réinitialisation du mot de passe",
                "Votre code de réinitialisation",
                "Utilisez le code ci-dessous pour réinitialiser votre mot de passe. Il est valable <strong>15 minutes</strong>.",
                "Si vous n'avez pas demandé cette réinitialisation, ignorez simplement cet email. Votre mot de passe ne sera pas modifié."),
            ["en"] = new("Reset your Salat Janaza password", "Password reset",
                "Your reset code",
                "Use the code below to reset your password. It is valid for <strong>15 minutes</strong>.",
                "If you did not request a password reset, simply ignore this email. Your password will not be changed."),
            ["ar"] = new("إعادة تعيين كلمة المرور — صلاة الجنازة", "إعادة تعيين كلمة المرور",
                "رمز إعادة التعيين الخاص بك",
                "استخدم الرمز أدناه لإعادة تعيين كلمة المرور الخاصة بك. صالح لمدة <strong>15 دقيقة</strong>.",
                "إذا لم تطلب إعادة تعيين كلمة المرور، فتجاهل هذا البريد الإلكتروني ببساطة. لن يتم تغيير كلمة المرور الخاصة بك.", Rtl: true),
            ["tr"] = new("Şifrenizi sıfırlayın — Salat Janaza", "Şifre sıfırlama",
                "Sıfırlama kodunuz",
                "Şifrenizi sıfırlamak için aşağıdaki kodu kullanın. <strong>15 dakika</strong> geçerlidir.",
                "Bu sıfırlama talebini siz yapmadıysanız, bu e-postayı dikkate almayın. Şifreniz değiştirilmeyecektir."),
            ["de"] = new("Passwort zurücksetzen — Salat Janaza", "Passwort zurücksetzen",
                "Ihr Reset-Code",
                "Verwenden Sie den folgenden Code, um Ihr Passwort zurückzusetzen. Er ist <strong>15 Minuten</strong> gültig.",
                "Wenn Sie diese Zurücksetzung nicht angefordert haben, ignorieren Sie einfach diese E-Mail. Ihr Passwort wird nicht geändert."),
            ["es"] = new("Restablece tu contraseña — Salat Janaza", "Restablecimiento de contraseña",
                "Tu código de restablecimiento",
                "Usa el código de abajo para restablecer tu contraseña. Es válido durante <strong>15 minutos</strong>.",
                "Si no solicitaste este restablecimiento, simplemente ignora este correo. Tu contraseña no será modificada."),
            ["it"] = new("Reimposta la tua password — Salat Janaza", "Reimpostazione password",
                "Il tuo codice di reimpostazione",
                "Usa il codice qui sotto per reimpostare la tua password. È valido per <strong>15 minuti</strong>.",
                "Se non hai richiesto questa reimpostazione, ignora semplicemente questa email. La tua password non verrà modificata."),
            ["pt"] = new("Redefina sua senha — Salat Janaza", "Redefinição de senha",
                "Seu código de redefinição",
                "Use o código abaixo para redefinir sua senha. Ele é válido por <strong>15 minutos</strong>.",
                "Se você não solicitou essa redefinição, simplesmente ignore este e-mail. Sua senha não será alterada."),
            ["ru"] = new("Сброс пароля — Salat Janaza", "Сброс пароля",
                "Ваш код сброса",
                "Используйте код ниже для сброса пароля. Он действителен <strong>15 минут</strong>.",
                "Если вы не запрашивали сброс пароля, просто проигнорируйте это письмо. Ваш пароль не будет изменён."),
            ["ja"] = new("パスワードのリセット — Salat Janaza", "パスワードリセット",
                "リセットコード",
                "以下のコードを使用してパスワードをリセットしてください。有効期限は<strong>15分</strong>です。",
                "このリセットをリクエストしていない場合は、このメールを無視してください。パスワードは変更されません。"),
            ["ko"] = new("비밀번호 재설정 — Salat Janaza", "비밀번호 재설정",
                "재설정 코드",
                "아래 코드를 사용하여 비밀번호를 재설정하세요. <strong>15분</strong> 동안 유효합니다.",
                "이 재설정을 요청하지 않으셨다면 이 이메일을 무시하세요. 비밀번호는 변경되지 않습니다."),
            ["ms"] = new("Tetapkan semula kata laluan anda — Salat Janaza", "Tetapan semula kata laluan",
                "Kod tetapan semula anda",
                "Gunakan kod di bawah untuk menetapkan semula kata laluan anda. Ia sah selama <strong>15 minit</strong>.",
                "Jika anda tidak meminta tetapan semula ini, abaikan sahaja e-mel ini. Kata laluan anda tidak akan diubah."),
            ["id"] = new("Atur ulang kata sandi Anda — Salat Janaza", "Atur ulang kata sandi",
                "Kode atur ulang Anda",
                "Gunakan kode di bawah untuk mengatur ulang kata sandi Anda. Berlaku selama <strong>15 menit</strong>.",
                "Jika Anda tidak meminta pengaturan ulang ini, abaikan saja email ini. Kata sandi Anda tidak akan diubah."),
            ["bn"] = new("আপনার পাসওয়ার্ড রিসেট করুন — Salat Janaza", "পাসওয়ার্ড রিসেট",
                "আপনার রিসেট কোড",
                "আপনার পাসওয়ার্ড রিসেট করতে নিচের কোডটি ব্যবহার করুন। এটি <strong>১৫ মিনিট</strong> বৈধ।",
                "আপনি যদি এই রিসেটের অনুরোধ না করে থাকেন, তাহলে এই ইমেইলটি উপেক্ষা করুন। আপনার পাসওয়ার্ড পরিবর্তন করা হবে না।"),
            ["ur"] = new("اپنا پاس ورڈ ری سیٹ کریں — Salat Janaza", "پاس ورڈ ری سیٹ",
                "آپ کا ری سیٹ کوڈ",
                "اپنا پاس ورڈ ری سیٹ کرنے کے لیے نیچے دیا گیا کوڈ استعمال کریں۔ یہ <strong>15 منٹ</strong> تک درست ہے۔",
                "اگر آپ نے یہ ری سیٹ درخواست نہیں کی تھی، تو اس ای میل کو نظر انداز کریں۔ آپ کا پاس ورڈ تبدیل نہیں کیا جائے گا۔", Rtl: true),
            ["bm"] = new("I ka gafe kura — Salat Janaza", "Gafe kura",
                "I ka code",
                "Code nunu kɛ i ka gafe kura. A ka kan <strong>miniti 15</strong> kɔnɔ.",
                "N'i ma nin ɲinigali kɛ, i ka nin bataki to. I ka gafe tɛna sɛbɛn."),
            ["nl"] = new("Wachtwoord opnieuw instellen — Salat Janaza", "Wachtwoord opnieuw instellen",
                "Uw resetcode",
                "Gebruik de onderstaande code om uw wachtwoord opnieuw in te stellen. De code is <strong>15 minuten</strong> geldig.",
                "Als u dit verzoek niet heeft gedaan, negeer dan gewoon deze e-mail. Uw wachtwoord wordt niet gewijzigd."),
        };

        private sealed record WelcomeL10n(
            string Subject, string Subtitle, string Greeting, string WelcomeLine,
            string AccountCreated, string FieldFirstName, string FieldLastName, string FieldEmail,
            string MissionTitle, string MissionText,
            string MeritsTitle, string Hadith, string HadithQuestion, string HadithAnswer,
            string HadithRef, string HadithNote, string Dua,
            bool Rtl = false);

        private static readonly Dictionary<string, WelcomeL10n> _welcomeStrings = new()
        {
            ["fr"] = new(
                Subject: "Bienvenue sur Salat Janaza",
                Subtitle: "Bienvenue",
                Greeting: "As salamou 3alaykoum wa rahmatulLahi wa barakatuh",
                WelcomeLine: "Bienvenue sur <strong style='color:#3A6B4A;'>Salat Janaza</strong>&nbsp;!",
                AccountCreated: "Votre compte a bien été créé. Voici un récapitulatif de vos informations :",
                FieldFirstName: "Prénom", FieldLastName: "Nom", FieldEmail: "Email",
                MissionTitle: "Notre mission",
                MissionText: "<strong>Salat Janaza</strong> a pour but de permettre aux défunts d'avoir le plus de monde possible lors de leur prière mortuaire, et de permettre aux croyants de ne pas manquer les immenses récompenses liées à la <em>Salat al-Janaza</em>.",
                MeritsTitle: "Les mérites de la Salat al-Janaza",
                Hadith: "Notre Prophète (ﷺ) a dit : « Celui qui suit le convoi funèbre du musulman, poussé par sa foi et désirant la rétribution de Dieu [auprès de Lui] jusqu'à ce qu'on prie sur le mort, aura comme récompense le poids d'un qirât, et celui qui reste jusqu'à ce qu'on l'enterre aura comme récompense le poids de deux qirât. »",
                HadithQuestion: "On lui demanda alors : « Ô Messager de Dieu, que sont les deux qirât ? »",
                HadithAnswer: "Il dit : « C'est l'équivalent [du poids] de deux grandes montagnes. »",
                HadithRef: "(Bukhârî 1239, Muslim 1570, les quatre sunans et Ahmad 8841)",
                HadithNote: "Et dans une autre version : « Le plus petit des qirât pèsera le poids de [la montagne] Uhud. »",
                Dua: "Qu'Allah nous accorde la sincérité dans nos actions."),
            ["en"] = new(
                Subject: "Welcome to Salat Janaza",
                Subtitle: "Welcome",
                Greeting: "As-salamu alaykum wa rahmatulLahi wa barakatuh",
                WelcomeLine: "Welcome to <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Your account has been created successfully. Here is a summary of your information:",
                FieldFirstName: "First name", FieldLastName: "Last name", FieldEmail: "Email",
                MissionTitle: "Our mission",
                MissionText: "<strong>Salat Janaza</strong> aims to help the deceased have as many people as possible at their funeral prayer, and to allow believers not to miss the immense rewards of <em>Salat al-Janaza</em>.",
                MeritsTitle: "The merits of Salat al-Janaza",
                Hadith: "Our Prophet (ﷺ) said: \"Whoever follows the funeral procession of a Muslim, driven by faith and hoping for reward from Allah, until the prayer is offered over the deceased, will receive a reward equal to one qirat; and whoever stays until the burial will receive two qirats.\"",
                HadithQuestion: "He was then asked: \"O Messenger of Allah, what are the two qirats?\"",
                HadithAnswer: "He said: \"They are like two great mountains.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, the four sunans and Ahmad 8841)",
                HadithNote: "In another version: \"The smallest of the two qirats weighs as much as Mount Uhud.\"",
                Dua: "May Allah grant us sincerity in our actions."),
            ["ar"] = new(
                Subject: "مرحباً بك في صلاة الجنازة",
                Subtitle: "أهلاً وسهلاً",
                Greeting: "السلام عليكم ورحمة الله وبركاته",
                WelcomeLine: "أهلاً وسهلاً بك في <strong style='color:#3A6B4A;'>صلاة الجنازة</strong>!",
                AccountCreated: "تم إنشاء حسابك بنجاح. إليك ملخص معلوماتك:",
                FieldFirstName: "الاسم الأول", FieldLastName: "اللقب", FieldEmail: "البريد الإلكتروني",
                MissionTitle: "رسالتنا",
                MissionText: "تهدف <strong>صلاة الجنازة</strong> إلى مساعدة المتوفى على الحصول على أكبر عدد ممكن من المصلين في صلاة جنازته، وتمكين المؤمنين من عدم تفويت الأجر العظيم لصلاة الجنازة.",
                MeritsTitle: "فضل صلاة الجنازة",
                Hadith: "قال النبي ﷺ: «مَنِ اتَّبَعَ جَنَازَةَ مُسْلِمٍ إِيمَاناً وَاحْتِسَاباً، وَكَانَ مَعَهُ حَتَّى يُصَلَّى عَلَيْهَا وَيُفْرَغَ مِنْ دَفْنِهَا، فَإِنَّهُ يَرْجِعُ مِنَ الأَجْرِ بِقِيرَاطَيْنِ، كُلُّ قِيرَاطٍ مِثْلُ أُحُدٍ، وَمَنْ صَلَّى عَلَيْهَا ثُمَّ رَجَعَ قَبْلَ أَنْ تُدْفَنَ، فَإِنَّهُ يَرْجِعُ بِقِيرَاطٍ»",
                HadithQuestion: "",
                HadithAnswer: "",
                HadithRef: "(البخاري ١٢٣٩، مسلم ١٥٧٠، السنن الأربعة وأحمد ٨٨٤١)",
                HadithNote: "",
                Dua: "جعلنا الله وإياكم من المخلصين في أعمالنا.",
                Rtl: true),
            ["tr"] = new(
                Subject: "Salat Janaza'ya Hoş Geldiniz",
                Subtitle: "Hoş Geldiniz",
                Greeting: "Es-selâmu aleyküm ve rahmetullahi ve berakâtüh",
                WelcomeLine: "<strong style='color:#3A6B4A;'>Salat Janaza</strong>'ya hoş geldiniz!",
                AccountCreated: "Hesabınız başarıyla oluşturuldu. Bilgilerinizin özeti:",
                FieldFirstName: "Ad", FieldLastName: "Soyad", FieldEmail: "E-posta",
                MissionTitle: "Misyonumuz",
                MissionText: "<strong>Salat Janaza</strong>, vefat edenlerin cenaze namazında mümkün olduğunca çok kişinin bulunmasını sağlamayı ve inananların <em>Salat al-Janaza</em>'nın büyük sevabını kaçırmamasını amaçlamaktadır.",
                MeritsTitle: "Cenaze Namazının Fazileti",
                Hadith: "Peygamberimiz (ﷺ) şöyle buyurdu: \"Kim bir Müslümanın cenazesini iman ve sevap ümidiyle takip eder, cenaze namazı kılınıp defnedilinceye kadar kalırsa, iki kırat sevap kazanır; sadece namazını kılıp dönen bir kırat sevap kazanır.\"",
                HadithQuestion: "\"Ey Allah'ın Rasulü, iki kırat nedir?\" diye soruldu.",
                HadithAnswer: "\"İki büyük dağ gibidir\" buyurdu.",
                HadithRef: "(Buhârî 1239, Müslim 1570, dört Sünnen ve Ahmed 8841)",
                HadithNote: "Başka bir rivayette: \"Kıratların en küçüği Uhud dağı kadardır.\"",
                Dua: "Allah amellerimizde bize ihlas nasip etsin."),
            ["de"] = new(
                Subject: "Willkommen bei Salat Janaza",
                Subtitle: "Willkommen",
                Greeting: "As-salamu alaikum wa rahmatullahi wa barakatuh",
                WelcomeLine: "Willkommen bei <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Ihr Konto wurde erfolgreich erstellt. Hier ist eine Zusammenfassung Ihrer Daten:",
                FieldFirstName: "Vorname", FieldLastName: "Nachname", FieldEmail: "E-Mail",
                MissionTitle: "Unsere Mission",
                MissionText: "<strong>Salat Janaza</strong> hat zum Ziel, dass möglichst viele Menschen am Totengebet für Verstorbene teilnehmen, damit Gläubige die immensen Belohnungen der <em>Salat al-Janaza</em> nicht verpassen.",
                MeritsTitle: "Die Verdienste des Totengebets",
                Hadith: "Unser Prophet (ﷺ) sagte: \"Wer dem Leichenzug eines Muslims aus Glauben und in der Hoffnung auf Belohnung folgt, bis das Gebet gesprochen und er begraben ist, erhält eine Belohnung von zwei Qirat; wer nur bis zum Gebet bleibt, erhält einen Qirat.\"",
                HadithQuestion: "Man fragte ihn: \"O Gesandter Allahs, was sind die zwei Qirat?\"",
                HadithAnswer: "Er sagte: \"Sie sind wie zwei große Berge.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, die vier Sunane und Ahmad 8841)",
                HadithNote: "In einer anderen Version: \"Der kleinste der Qirat wiegt so viel wie der Berg Uhud.\"",
                Dua: "Möge Allah uns Aufrichtigkeit in unseren Taten schenken."),
            ["es"] = new(
                Subject: "Bienvenido a Salat Janaza",
                Subtitle: "Bienvenido",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                WelcomeLine: "¡Bienvenido a <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Tu cuenta ha sido creada con éxito. Aquí tienes un resumen de tu información:",
                FieldFirstName: "Nombre", FieldLastName: "Apellido", FieldEmail: "Correo electrónico",
                MissionTitle: "Nuestra misión",
                MissionText: "<strong>Salat Janaza</strong> tiene como objetivo que el mayor número posible de personas esté presente en la oración fúnebre del difunto, y permitir que los creyentes no pierdan las inmensas recompensas de la <em>Salat al-Janaza</em>.",
                MeritsTitle: "Los méritos de la Salat al-Janaza",
                Hadith: "Nuestro Profeta (ﷺ) dijo: \"Quien siga el cortejo fúnebre de un musulmán, movido por la fe y esperando la recompensa de Allah, hasta que se realice la oración y sea sepultado, recibirá una recompensa de dos qiratos; quien solo asista a la oración recibirá un qirato.\"",
                HadithQuestion: "Le preguntaron: \"¡Oh Mensajero de Allah! ¿Qué son los dos qiratos?\"",
                HadithAnswer: "Dijo: \"Son como dos grandes montañas.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, los cuatro sunanes y Ahmad 8841)",
                HadithNote: "En otra versión: \"El menor de los qiratos pesa tanto como el monte Uhud.\"",
                Dua: "Que Allah nos conceda sinceridad en nuestras acciones."),
            ["it"] = new(
                Subject: "Benvenuto su Salat Janaza",
                Subtitle: "Benvenuto",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                WelcomeLine: "Benvenuto su <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Il tuo account è stato creato con successo. Ecco un riepilogo delle tue informazioni:",
                FieldFirstName: "Nome", FieldLastName: "Cognome", FieldEmail: "Email",
                MissionTitle: "La nostra missione",
                MissionText: "<strong>Salat Janaza</strong> mira a far sì che il maggior numero possibile di persone sia presente alla preghiera funebre del defunto, e a permettere ai credenti di non perdere le immense ricompense della <em>Salat al-Janaza</em>.",
                MeritsTitle: "I meriti della Salat al-Janaza",
                Hadith: "Il nostro Profeta (ﷺ) disse: \"Chi segue il corteo funebre di un musulmano, spinto dalla fede e sperando nella ricompensa di Allah, fino a quando si prega sul defunto e viene sepolto, riceverà una ricompensa pari a due qirat; chi rimane solo fino alla preghiera riceverà un qirat.\"",
                HadithQuestion: "Gli fu chiesto: \"O Messaggero di Allah, cosa sono i due qirat?\"",
                HadithAnswer: "Disse: \"Sono come due grandi montagne.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, i quattro sunan e Ahmad 8841)",
                HadithNote: "In un'altra versione: \"Il minore dei qirat pesa quanto il monte Uhud.\"",
                Dua: "Che Allah ci conceda sincerità nelle nostre azioni."),
            ["pt"] = new(
                Subject: "Bem-vindo ao Salat Janaza",
                Subtitle: "Bem-vindo",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                WelcomeLine: "Bem-vindo ao <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Sua conta foi criada com sucesso. Aqui está um resumo das suas informações:",
                FieldFirstName: "Nome", FieldLastName: "Sobrenome", FieldEmail: "E-mail",
                MissionTitle: "Nossa missão",
                MissionText: "<strong>Salat Janaza</strong> tem como objetivo que o maior número possível de pessoas esteja presente na oração fúnebre do falecido, e permitir que os crentes não percam as imensas recompensas da <em>Salat al-Janaza</em>.",
                MeritsTitle: "Os méritos da Salat al-Janaza",
                Hadith: "Nosso Profeta (ﷺ) disse: \"Quem seguir o cortejo fúnebre de um muçulmano, movido pela fé e esperando a recompensa de Allah, até que a oração seja realizada e ele seja enterrado, receberá uma recompensa de dois qirats; quem ficar apenas até a oração receberá um qirat.\"",
                HadithQuestion: "Perguntaram-lhe: \"Ó Mensageiro de Allah, o que são os dois qirats?\"",
                HadithAnswer: "Ele disse: \"São como duas grandes montanhas.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, os quatro sunans e Ahmad 8841)",
                HadithNote: "Em outra versão: \"O menor dos qirats pesa tanto quanto o monte Uhud.\"",
                Dua: "Que Allah nos conceda sinceridade em nossas ações."),
            ["ru"] = new(
                Subject: "Добро пожаловать в Salat Janaza",
                Subtitle: "Добро пожаловать",
                Greeting: "Ас-саляму алейкум ва рахматуллахи ва баракатух",
                WelcomeLine: "Добро пожаловать в <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Ваш аккаунт успешно создан. Вот сводка ваших данных:",
                FieldFirstName: "Имя", FieldLastName: "Фамилия", FieldEmail: "Эл. почта",
                MissionTitle: "Наша миссия",
                MissionText: "<strong>Salat Janaza</strong> стремится к тому, чтобы как можно больше людей присутствовало на погребальной молитве, и помогает верующим не упустить огромное вознаграждение за <em>Салят аль-Джаназа</em>.",
                MeritsTitle: "Достоинства Саляту аль-Джаназа",
                Hadith: "Наш Пророк (ﷺ) сказал: «Тот, кто следует за похоронной процессией мусульманина с верой и надеждой на вознаграждение от Аллаха, до тех пор, пока не совершит молитву по умершему и не похоронит его, получит награду в два кирата; тот же, кто уйдёт после молитвы, — один кират».",
                HadithQuestion: "Его спросили: «О Посланник Аллаха, что такое два кирата?»",
                HadithAnswer: "Он сказал: «Подобны двум огромным горам».",
                HadithRef: "(Бухари 1239, Муслим 1570, четыре Сунана и Ахмад 8841)",
                HadithNote: "В другой версии: «Меньший из двух киратов весит столько, сколько гора Ухуд».",
                Dua: "Да дарует нам Аллах искренность в наших делах."),
            ["ja"] = new(
                Subject: "Salat Janazaへようこそ",
                Subtitle: "ようこそ",
                Greeting: "アッサラーム・アライクム・ワ・ラフマトゥッラーヒ・ワ・バラカートゥフ",
                WelcomeLine: "<strong style='color:#3A6B4A;'>Salat Janaza</strong>へようこそ！",
                AccountCreated: "アカウントが正常に作成されました。お客様の情報の概要です：",
                FieldFirstName: "名", FieldLastName: "姓", FieldEmail: "メール",
                MissionTitle: "私たちのミッション",
                MissionText: "<strong>Salat Janaza</strong>は、故人のジャナザ礼拝にできるだけ多くの人が参加できるようにし、信者が<em>Salat al-Janaza</em>の莫大な報酬を逃さないようにすることを目指しています。",
                MeritsTitle: "ジャナザ礼拝の功徳",
                Hadith: "預言者（ﷺ）は仰いました：「信仰と報酬を求めてムスリムの葬儀行列に従い、礼拝が行われ埋葬されるまでいた者には二キラトの報酬があり、礼拝後に戻った者には一キラトの報酬がある。」",
                HadithQuestion: "「アッラーの使徒よ、二キラトとは何ですか？」と尋ねられました。",
                HadithAnswer: "「二つの大きな山のようなものだ」と仰いました。",
                HadithRef: "(ブハーリー1239、ムスリム1570、四スナン、アフマド8841)",
                HadithNote: "別の伝承では：「小さい方のキラトはウフドの山に相当する。」",
                Dua: "アッラーが私たちの行いに誠実さをお与えくださいますように。"),
            ["ko"] = new(
                Subject: "Salat Janaza에 오신 것을 환영합니다",
                Subtitle: "환영합니다",
                Greeting: "앗살라무 알라이쿰 와 라흐마툴라히 와 바라카투후",
                WelcomeLine: "<strong style='color:#3A6B4A;'>Salat Janaza</strong>에 오신 것을 환영합니다!",
                AccountCreated: "계정이 성공적으로 생성되었습니다. 정보 요약입니다:",
                FieldFirstName: "이름", FieldLastName: "성", FieldEmail: "이메일",
                MissionTitle: "우리의 사명",
                MissionText: "<strong>Salat Janaza</strong>는 고인의 장례 예배에 가능한 한 많은 사람이 참여할 수 있도록 하고, 신자들이 <em>Salat al-Janaza</em>의 큰 보상을 놓치지 않도록 돕는 것을 목표로 합니다.",
                MeritsTitle: "장례 예배의 덕목",
                Hadith: "예언자(ﷺ)께서 말씀하셨습니다: \"믿음과 알라의 보상을 바라며 무슬림의 장례 행렬을 따라가 예배가 드려지고 매장될 때까지 함께한 자는 두 키라트의 보상을 받을 것이며, 예배 후 돌아간 자는 한 키라트의 보상을 받을 것이다.\"",
                HadithQuestion: "\"오 알라의 사도여, 두 키라트란 무엇입니까?\"라고 물었습니다.",
                HadithAnswer: "\"두 개의 큰 산과 같다\"고 대답하셨습니다.",
                HadithRef: "(부하리 1239, 무슬림 1570, 사대 수난 및 아흐마드 8841)",
                HadithNote: "다른 전승에서는: \"더 작은 키라트는 우후드 산만큼 무겁다.\"",
                Dua: "알라께서 우리의 행동에 성실함을 허락해 주시길 바랍니다."),
            ["ms"] = new(
                Subject: "Selamat datang ke Salat Janaza",
                Subtitle: "Selamat Datang",
                Greeting: "Assalamualaikum warahmatullahi wabarakatuh",
                WelcomeLine: "Selamat datang ke <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Akaun anda telah berjaya dicipta. Berikut adalah ringkasan maklumat anda:",
                FieldFirstName: "Nama pertama", FieldLastName: "Nama keluarga", FieldEmail: "E-mel",
                MissionTitle: "Misi kami",
                MissionText: "<strong>Salat Janaza</strong> bertujuan untuk membantu si mati mendapat seramai mungkin orang di solat jenazahnya, dan membolehkan orang beriman tidak terlepas ganjaran besar <em>Solat al-Janaza</em>.",
                MeritsTitle: "Kelebihan Solat al-Janaza",
                Hadith: "Nabi kami (ﷺ) bersabda: \"Sesiapa yang mengikuti jenazah seorang Muslim dengan iman dan mengharap pahala daripada Allah sehingga solat dilaksanakan dan dikebumikan, akan mendapat pahala dua qirat; sesiapa yang pulang selepas solat akan mendapat satu qirat.\"",
                HadithQuestion: "Baginda ditanya: \"Wahai Rasulullah, apakah dua qirat itu?\"",
                HadithAnswer: "Baginda menjawab: \"Seperti dua bukit yang besar.\"",
                HadithRef: "(Bukhari 1239, Muslim 1570, empat Sunan dan Ahmad 8841)",
                HadithNote: "Dalam riwayat lain: \"Qirat yang paling kecil sebesar Gunung Uhud.\"",
                Dua: "Semoga Allah mengurniakan kita keikhlasan dalam amalan kita."),
            ["id"] = new(
                Subject: "Selamat datang di Salat Janaza",
                Subtitle: "Selamat Datang",
                Greeting: "Assalamu'alaikum warahmatullahi wabarakatuh",
                WelcomeLine: "Selamat datang di <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Akun Anda telah berhasil dibuat. Berikut ringkasan informasi Anda:",
                FieldFirstName: "Nama depan", FieldLastName: "Nama belakang", FieldEmail: "Email",
                MissionTitle: "Misi kami",
                MissionText: "<strong>Salat Janaza</strong> bertujuan untuk membantu almarhum mendapatkan orang sebanyak mungkin di shalat jenazahnya, dan memungkinkan umat beriman untuk tidak melewatkan pahala besar <em>Salat al-Janaza</em>.",
                MeritsTitle: "Keutamaan Salat al-Janaza",
                Hadith: "Nabi kita (ﷺ) bersabda: \"Barangsiapa mengikuti jenazah seorang Muslim dengan iman dan mengharap pahala dari Allah hingga dishalatkan dan dikuburkan, akan mendapat pahala dua qirat; barangsiapa pulang setelah shalat akan mendapat satu qirat.\"",
                HadithQuestion: "Beliau ditanya: \"Ya Rasulullah, apa itu dua qirat?\"",
                HadithAnswer: "Beliau menjawab: \"Seperti dua gunung yang besar.\"",
                HadithRef: "(Bukhari 1239, Muslim 1570, empat Sunan dan Ahmad 8841)",
                HadithNote: "Dalam riwayat lain: \"Qirat yang paling kecil sebesar Gunung Uhud.\"",
                Dua: "Semoga Allah menganugerahkan keikhlasan dalam amal kita."),
            ["bn"] = new(
                Subject: "Salat Janaza-তে আপনাকে স্বাগতম",
                Subtitle: "স্বাগতম",
                Greeting: "আস্‌সালামু আলাইকুম ওয়া রাহমাতুল্লাহি ওয়া বারাকাতুহ",
                WelcomeLine: "<strong style='color:#3A6B4A;'>Salat Janaza</strong>-তে আপনাকে স্বাগতম!",
                AccountCreated: "আপনার অ্যাকাউন্ট সফলভাবে তৈরি হয়েছে। আপনার তথ্যের সারসংক্ষেপ:",
                FieldFirstName: "প্রথম নাম", FieldLastName: "পদবি", FieldEmail: "ইমেইল",
                MissionTitle: "আমাদের লক্ষ্য",
                MissionText: "<strong>Salat Janaza</strong>-এর লক্ষ্য হলো মৃত ব্যক্তির জানাজার নামাজে যতটা সম্ভব বেশি মানুষের উপস্থিতি নিশ্চিত করা এবং বিশ্বাসীদের <em>সালাত আল-জানাজা</em>-র বিশাল পুরস্কার থেকে বঞ্চিত না হতে সাহায্য করা।",
                MeritsTitle: "সালাত আল-জানাজার ফজিলত",
                Hadith: "আমাদের নবী (ﷺ) বলেছেন: \"যে ব্যক্তি ঈমান ও সওয়াবের আশায় কোনো মুসলমানের জানাজায় অংশগ্রহণ করে নামাজ পড়া ও দাফন করা পর্যন্ত থাকে, সে দুই কিরাত সওয়াব পাবে; আর যে শুধু নামাজ পর্যন্ত থাকে, সে এক কিরাত সওয়াব পাবে।\"",
                HadithQuestion: "জিজ্ঞেস করা হলো: \"হে আল্লাহর রাসুল, দুই কিরাত কী?\"",
                HadithAnswer: "তিনি বললেন: \"দুটি বড় পাহাড়ের সমান।\"",
                HadithRef: "(বুখারি ১২৩৯, মুসলিম ১৫৭০, চার সুনান ও আহমাদ ৮৮৪১)",
                HadithNote: "অন্য বর্ণনায়: \"ছোট কিরাতটি উহুদ পাহাড়ের সমান।\"",
                Dua: "আল্লাহ আমাদের আমলে ইখলাস দান করুন।"),
            ["ur"] = new(
                Subject: "Salat Janaza میں خوش آمدید",
                Subtitle: "خوش آمدید",
                Greeting: "السلام علیکم ورحمۃ اللہ وبرکاتہ",
                WelcomeLine: "<strong style='color:#3A6B4A;'>Salat Janaza</strong> میں خوش آمدید!",
                AccountCreated: "آپ کا اکاؤنٹ کامیابی سے بنا دیا گیا ہے۔ آپ کی معلومات کا خلاصہ:",
                FieldFirstName: "پہلا نام", FieldLastName: "خاندانی نام", FieldEmail: "ای میل",
                MissionTitle: "ہمارا مقصد",
                MissionText: "<strong>Salat Janaza</strong> کا مقصد ہے کہ میت کی نماز جنازہ میں زیادہ سے زیادہ لوگ شریک ہوں اور مومنین <em>صلاۃ الجنازہ</em> کے عظیم اجر سے محروم نہ رہیں۔",
                MeritsTitle: "نماز جنازہ کی فضیلت",
                Hadith: "ہمارے نبی (ﷺ) نے فرمایا: \"جو شخص کسی مسلمان کے جنازے میں ایمان اور ثواب کی نیت سے شریک ہو اور نماز و دفن تک رہے، اسے دو قیراط ثواب ملے گا؛ اور جو صرف نماز تک رہے، اسے ایک قیراط ملے گا۔\"",
                HadithQuestion: "پوچھا گیا: \"اے اللہ کے رسول، دو قیراط کیا ہیں؟\"",
                HadithAnswer: "آپ نے فرمایا: \"دو بڑے پہاڑوں جیسے۔\"",
                HadithRef: "(بخاری ۱۲۳۹، مسلم ۱۵۷۰، چاروں سنن اور احمد ۸۸۴۱)",
                HadithNote: "ایک اور روایت میں: \"چھوٹا قیراط بھی احد پہاڑ کے برابر ہے۔\"",
                Dua: "اللہ ہمیں اپنے اعمال میں اخلاص عطا فرمائے۔",
                Rtl: true),
            ["bm"] = new(
                Subject: "Aw bisimila Salat Janaza",
                Subtitle: "Bisimila",
                Greeting: "I ni ce, Ala k'aw sariya",
                WelcomeLine: "<strong style='color:#3A6B4A;'>Salat Janaza</strong> — aw bisimila!",
                AccountCreated: "I ka compte dalen don. I ka kunnafoni kɛrɛnkɛrɛnnen:",
                FieldFirstName: "Tɔgɔ", FieldLastName: "Jamu", FieldEmail: "E-mail",
                MissionTitle: "An ka laɲini",
                MissionText: "<strong>Salat Janaza</strong> b'a fɛ ka mɔgɔ caaman sɔrɔ janaza seliw la, k'a to dɛmɛbagaw ka se ka <em>Salat al-Janaza</em> ka tiiɲɛ sɔrɔ.",
                MeritsTitle: "Janaza seli ka tiiɲɛ",
                Hadith: "An ka Annabi (ﷺ) y'a fɔ: \"Mɔgɔ min bɛ taga musulumani ka janaza kɛ n'a ka dɔnniya ni latigɛ ye Ala fe, a bɛ seli kɛ ani a ka suu sɔgɔsɔgɔ fo sɔgɔ laban na, ale bɛ taamasiyɛn fila sɔrɔ; ni a tagara seli kɛ dɔrɔn, a bɛ kelen sɔrɔ.\"",
                HadithQuestion: "A ɲinɔgɔnna: \"Ala ka kira, taamasiyɛn fila ye mun ye?\"",
                HadithAnswer: "A y'a jaabi: \"Kulu ba fila ye.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, sunan naani ani Ahmad 8841)",
                HadithNote: "Kɔrɔ fɔli wɛrɛ la: \"Taamasiyɛn fitini ye Uhud kulu ye.\"",
                Dua: "Ala ka tiiɲɛ di an ma an ka baara la."),
            ["nl"] = new(
                Subject: "Welkom bij Salat Janaza",
                Subtitle: "Welkom",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                WelcomeLine: "Welkom bij <strong style='color:#3A6B4A;'>Salat Janaza</strong>!",
                AccountCreated: "Uw account is succesvol aangemaakt. Hier is een overzicht van uw gegevens:",
                FieldFirstName: "Voornaam", FieldLastName: "Achternaam", FieldEmail: "E-mail",
                MissionTitle: "Onze missie",
                MissionText: "<strong>Salat Janaza</strong> heeft als doel zoveel mogelijk mensen aanwezig te laten zijn bij het dodengebed voor de overledene, en gelovigen te helpen de immense beloningen van de <em>Salat al-Janaza</em> niet te missen.",
                MeritsTitle: "De verdiensten van Salat al-Janaza",
                Hadith: "Onze Profeet (ﷺ) zei: \"Wie de begrafenisstoet van een moslim volgt uit geloof en in de hoop op beloning van Allah, totdat het gebed is verricht en hij begraven is, ontvangt een beloning van twee qirat; wie alleen blijft tot het gebed ontvangt één qirat.\"",
                HadithQuestion: "Hem werd gevraagd: \"O Boodschapper van Allah, wat zijn de twee qirat?\"",
                HadithAnswer: "Hij zei: \"Ze zijn als twee grote bergen.\"",
                HadithRef: "(Bukhârî 1239, Muslim 1570, de vier Sunans en Ahmad 8841)",
                HadithNote: "In een andere overlevering: \"De kleinste qirat weegt zoveel als de berg Uhud.\"",
                Dua: "Moge Allah ons oprechtheid schenken in onze daden."),
        };

        public AuthController(UserManager<ApplicationUser> userManager, IConfiguration config, ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _config = config;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            var lang = NormalizeLanguage(req.Language);
            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                Prenom = req.Prenom,
                Nom = req.Nom,
                EmailConfirmed = true,
                Language = lang,
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            _ = Task.Run(async () =>
            {
                try { await SendWelcomeEmailAsync(req.Email, req.Prenom, req.Nom, lang); }
                catch (Exception ex) { _logger.LogError(ex, "Erreur envoi email bienvenue à {Email}", req.Email); }
            });

            _ = Task.Run(async () =>
            {
                try { await SendTelegramNewUserAsync(req.Prenom, req.Nom, req.Email, lang); }
                catch (Exception ex) { _logger.LogError(ex, "Erreur Telegram nouvel utilisateur {Email}", req.Email); }
            });

            return Ok(new { userId = user.Id, email = user.Email, message = "Compte créé avec succès." });
        }

        [HttpDelete("account/internal/{identityUserId}")]
        public async Task<IActionResult> DeleteAccountInternal(string identityUserId)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user is null) return NoContent();

            await _userManager.DeleteAsync(user);
            return NoContent();
        }

        [HttpPut("account/internal/{identityUserId}/role")]
        public async Task<IActionResult> UpdateRoleInternal(string identityUserId, [FromBody] UpdateRoleRequest req)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user is null) return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            var roleManager = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
            var newRole = string.IsNullOrWhiteSpace(req.Role) ? "User" : req.Role;
            if (!await roleManager.RoleExistsAsync(newRole))
                await roleManager.CreateAsync(new IdentityRole(newRole));
            await _userManager.AddToRoleAsync(user, newRole);

            return Ok();
        }

        [HttpPut("account/internal/{identityUserId}/language")]
        public async Task<IActionResult> UpdateLanguageInternal(string identityUserId, [FromBody] UpdateLanguageRequest req)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user is null) return NotFound();
            user.Language = NormalizeLanguage(req.Language);
            await _userManager.UpdateAsync(user);
            return Ok();
        }

        [HttpPost("account/internal/create")]
        public async Task<IActionResult> CreateInternal([FromBody] InternalCreateRequest req)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var existing = await _userManager.FindByEmailAsync(req.Email);
            if (existing is not null)
                return BadRequest(new { error = "Un compte avec cet email existe déjà." });

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                Prenom = req.Prenom,
                Nom = req.Nom,
                EmailConfirmed = true,
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Erreur lors de la création.";
                return BadRequest(new { error });
            }

            var roleManager = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
            var role = string.IsNullOrWhiteSpace(req.Role) ? "User" : req.Role;
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
            await _userManager.AddToRoleAsync(user, role);

            return Ok(new { userId = user.Id, email = user.Email });
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (User.Identity?.IsAuthenticated != true) return Unauthorized();
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return NotFound();
            return Ok(new { user.Id, user.Email, user.Prenom, user.Nom });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var user = await _userManager.FindByEmailAsync(req.Email);
            if (user is null) return BadRequest(new { error = "Utilisateur introuvable." });

            var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Erreur lors du changement de mot de passe.";
                return BadRequest(new { error });
            }

            return Ok(new { message = "Mot de passe modifié avec succès." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            var user = await _userManager.FindByEmailAsync(req.Email);
            if (user is null)
                return Ok(new { message = "Si ce compte existe, un code a été envoyé par email." });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = new Random().Next(100000, 999999).ToString();
            _resetCodes[req.Email.ToLower()] = (code, token, DateTime.UtcNow.AddMinutes(15));

            var resetLang = NormalizeLanguage(!string.IsNullOrWhiteSpace(req.Language) ? req.Language : user.Language);
            try
            {
                await SendResetEmailAsync(req.Email, code, resetLang);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur envoi email réinitialisation à {Email}", req.Email);
                return StatusCode(500, new { error = "Erreur lors de l'envoi de l'email." });
            }

            return Ok(new { message = "Si ce compte existe, un code a été envoyé par email." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
        {
            var key = req.Email.ToLower();
            if (!_resetCodes.TryGetValue(key, out var entry))
                return BadRequest(new { error = "Code invalide ou expiré." });

            if (entry.Expiry < DateTime.UtcNow)
            {
                _resetCodes.TryRemove(key, out _);
                return BadRequest(new { error = "Code expiré. Faites une nouvelle demande." });
            }

            if (entry.Code != req.Code)
                return BadRequest(new { error = "Code incorrect." });

            var user = await _userManager.FindByEmailAsync(req.Email);
            if (user is null) return BadRequest(new { error = "Utilisateur introuvable." });

            var result = await _userManager.ResetPasswordAsync(user, entry.Token, req.NewPassword);
            _resetCodes.TryRemove(key, out _);

            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Erreur lors de la réinitialisation.";
                return BadRequest(new { error });
            }

            return Ok(new { message = "Mot de passe réinitialisé avec succès." });
        }

        private static MimeMessage BuildBaseMessage(IConfiguration config, string recipientEmail, string subject)
        {
            var settings = config.GetSection("EmailSettings");
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(settings["SenderEmail"]!));
            msg.To.Add(MailboxAddress.Parse(recipientEmail));
            msg.Subject = subject;
            return msg;
        }

        private static MimeEntity BuildBody(string html, string logoPath)
        {
            var htmlPart = new TextPart("html") { Text = html };
            if (!System.IO.File.Exists(logoPath)) return htmlPart;

            var image = new MimePart("image", "png")
            {
                Content = new MimeContent(System.IO.File.OpenRead(logoPath)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
                ContentId = "logo",
            };
            return new MultipartRelated { htmlPart, image };
        }

        private static string LogoPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "acc1.png");

        private static string EmailHeader(string subtitle) => $@"
  <div style='background:#3A6B4A;padding:20px 28px;'>
    <table cellpadding='0' cellspacing='0' border='0' width='100%'>
      <tr>
        <td width='56' valign='middle'>
          <img src='cid:logo' width='48' height='48' alt='' style='display:block;border-radius:8px;'/>
        </td>
        <td valign='middle' style='padding-left:14px;'>
          <div style='color:#fff;font-size:20px;font-weight:800;letter-spacing:0.3px;font-family:Georgia,serif;'>Salat Janaza</div>
          <div style='color:rgba(255,255,255,0.65);font-size:11px;letter-spacing:1.5px;text-transform:uppercase;margin-top:2px;'>{subtitle}</div>
        </td>
      </tr>
    </table>
  </div>";

        private static string EmailFooter(string language = "fr")
        {
            var text = language switch {
                "en" => "Questions?",
                "ar" => "أسئلة؟",
                "tr" => "Sorularınız mı var?",
                "de" => "Fragen?",
                "es" => "¿Preguntas?",
                "it" => "Domande?",
                "pt" => "Perguntas?",
                "ru" => "Вопросы?",
                "ja" => "ご質問は？",
                "ko" => "질문이 있으신가요?",
                "ms" => "Soalan?",
                "id" => "Pertanyaan?",
                "bn" => "প্রশ্ন আছে?",
                "ur" => "سوالات؟",
                "bm" => "Ɲinigaliw?",
                "nl" => "Vragen?",
                _ => "Une question ?"
            };
            return $@"
  <div style='background:#3A6B4A;padding:18px 28px;text-align:center;'>
    <p style='color:rgba(255,255,255,0.9);font-size:13px;margin:0;'>
      {text} <a href='mailto:support@salatjanaza.org' style='color:#ffffff !important;font-weight:700;text-decoration:none;'>support@salatjanaza.org</a>
    </p>
  </div>";
        }

        private static string NormalizeLanguage(string? lang) => lang?.ToLower() switch {
            "fr" => "fr", "en" => "en", "ar" => "ar",
            "tr" => "tr", "ja" => "ja", "ko" => "ko",
            "ms" => "ms", "ur" => "ur", "id" => "id", "bn" => "bn", "ru" => "ru", "pt" => "pt", "de" => "de", "it" => "it", "es" => "es",
            "bm" => "bm", "nl" => "nl",
            _ => "en"
        };

        private async Task SendEmailAsync(MimeMessage message)
        {
            var settings = _config.GetSection("EmailSettings");
            using var client = new SmtpClient();
            await client.ConnectAsync(settings["SmtpHost"]!, int.Parse(settings["SmtpPort"]!), SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(settings["SenderEmail"]!, settings["SenderPassword"]!);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private async Task SendWelcomeEmailAsync(string recipientEmail, string prenom, string nom, string language = "fr")
        {
            var welcomeL = _welcomeStrings.TryGetValue(language, out var wl) ? wl : _welcomeStrings["en"];
            var subject = welcomeL.Subject;
            var html = BuildWelcomeHtml(prenom, nom, recipientEmail, language);
            var message = BuildBaseMessage(_config, recipientEmail, subject);
            message.Body = BuildBody(html, LogoPath);
            await SendEmailAsync(message);
        }

        private string BuildWelcomeHtml(string prenom, string nom, string email, string lang)
        {
            var l = _welcomeStrings.TryGetValue(lang, out var v) ? v : _welcomeStrings["en"];
            var dir = l.Rtl ? "direction:rtl;" : "";
            var borderSide = l.Rtl ? "border-right" : "border-left";
            var borderRadius = l.Rtl ? "8px 0 0 8px" : "0 8px 8px 0";
            var rtlSide = l.Rtl ? "right" : "left";
            var haditRef = !string.IsNullOrEmpty(l.HadithRef)
                ? $"<p style='color:#999;font-size:12px;font-style:italic;margin:0 0 12px;direction:ltr;text-align:{rtlSide};'>{l.HadithRef}</p>"
                : "";
            var hadithQuestion = !string.IsNullOrEmpty(l.HadithQuestion)
                ? $"<p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 10px;'>{l.HadithQuestion}</p>"
                : "";
            var hadithAnswer = !string.IsNullOrEmpty(l.HadithAnswer)
                ? $"<blockquote style='{borderSide}:4px solid #3A6B4A;padding-{rtlSide}:14px;margin:0 0 10px;color:#444;font-style:italic;font-size:14px;line-height:1.8;'>{l.HadithAnswer}</blockquote>"
                : "";
            var hadithNote = !string.IsNullOrEmpty(l.HadithNote)
                ? $"<p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 14px;font-style:italic;'>{l.HadithNote}</p>"
                : "";
            return $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;{dir}'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader(l.Subtitle)}
  <div style='background:#fff;padding:24px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0 0 8px;'>{l.Greeting} <strong>{prenom}</strong>,</p>
    <p style='color:#222;font-size:16px;margin:0;'>{l.WelcomeLine}</p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 16px;'>{l.AccountCreated}</p>
    <div style='background:#f4f8f4;{borderSide}:4px solid #3A6B4A;border-radius:{borderRadius};padding:14px 18px;'>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>{l.FieldFirstName} :</strong> {prenom}</p>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>{l.FieldLastName} :</strong> {nom}</p>
      <p style='margin:0;font-size:14px;color:#444;'><strong>{l.FieldEmail} :</strong> {email}</p>
    </div>
  </div>
  <div style='background:#f9fcf9;padding:20px 28px;border-top:1px solid #e8f0e9;border-bottom:1px solid #e8f0e9;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 10px;'>{l.MissionTitle}</h2>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>{l.MissionText}</p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 14px;'>{l.MeritsTitle}</h2>
    <blockquote style='{borderSide}:4px solid #3A6B4A;padding-{rtlSide}:14px;margin:0 0 12px;color:#444;font-style:italic;font-size:14px;line-height:1.8;'>{l.Hadith}</blockquote>
    {hadithQuestion}
    {hadithAnswer}
    {haditRef}
    {hadithNote}
    <p style='color:#3A6B4A;font-size:14px;font-weight:600;margin:0;'>{l.Dua}</p>
  </div>
  {EmailFooter(lang)}
</div>
</body></html>";
        }

        private async Task SendResetEmailAsync(string recipientEmail, string code, string language = "fr")
        {
            var resetL = _resetStrings.TryGetValue(language, out var rl) ? rl : _resetStrings["en"];
            var subject = resetL.Subject;
            var html = BuildResetHtml(code, language);
            var message = BuildBaseMessage(_config, recipientEmail, subject);
            message.Body = BuildBody(html, LogoPath);
            await SendEmailAsync(message);
        }

        private string BuildResetHtml(string code, string lang)
        {
            var l = _resetStrings.TryGetValue(lang, out var v) ? v : _resetStrings["en"];
            var dir = l.Rtl ? "direction:rtl;" : "";
            return $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;{dir}'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader(l.Subtitle)}
  <div style='background:#fff;padding:28px;'>
    <p style='color:#222;font-size:15px;font-weight:600;margin:0 0 10px;'>{l.Title}</p>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 24px;'>{l.Body}</p>
    <div style='background:#f4f8f4;border:2px solid #3A6B4A;border-radius:12px;padding:26px 16px;text-align:center;margin:0 0 24px;'>
      <p style='font-size:46px;font-weight:900;letter-spacing:12px;color:#3A6B4A;margin:0;font-family:monospace;direction:ltr;'>{code}</p>
    </div>
    <p style='color:#999;font-size:13px;line-height:1.6;margin:0;'>{l.Footer}</p>
  </div>
  {EmailFooter(lang)}
</div>
</body></html>";
        }

        private Task SendTelegramNewUserAsync(string prenom, string nom, string email, string lang)
            => QabrWebApp.IdentityServer.Helpers.TelegramHelper.SendNewUserAsync(_config, prenom, nom, email, lang);
    }

    public class RegisterRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        [Required]
        public string Prenom { get; set; } = string.Empty;
        [Required]
        public string Nom { get; set; } = string.Empty;
        public string Language { get; set; } = "fr";
    }

    public class ChangePasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        public string? Language { get; set; }
    }

    public class ResetPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string Code { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class InternalCreateRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        [Required]
        public string Prenom { get; set; } = string.Empty;
        [Required]
        public string Nom { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
    }

    public class UpdateRoleRequest
    {
        [Required]
        public string Role { get; set; } = "User";
    }

    public class UpdateLanguageRequest
    {
        [Required]
        public string Language { get; set; } = "fr";
    }
}
