export interface FaqItem {
  question: string;
  answer: string;
}

// Questions SPECIFIC to each category page. The home page FAQ explains the
// platform ("how often are prices updated"); these are about the product
// itself and target what people really type into Google ("does creatine cause
// hair loss"). Two gains: (1) long-tail search traffic lands directly on the
// listing page, (2) the category page stops being "just a product list",
// which was the root cause of "Crawled - currently not indexed".
//
// TONE: honest, no hype, no firm medical claims. Saying "it depends, ask a
// doctor" beats making something up.
export const CATEGORY_FAQS: Record<string, FaqItem[]> = {
  'protein-powder': [
    {
      question: 'What should I look for when choosing a protein powder?',
      answer:
        'Look at the cost of a serving and how many grams of protein that serving has, not the tub price. Of two products at the same price, one can have noticeably less protein per serving. The serving size and number of servings on the label are the basis for this comparison.',
    },
    {
      question: 'Is whey isolate better than concentrate?',
      answer:
        'Both are whey; the difference is processing. Isolate is filtered more, so it is higher in protein and lower in lactose and fat, an advantage if you are lactose sensitive. Concentrate is cheaper and enough for most people. "More expensive is better for everyone" isn\'t true.',
    },
    {
      question: 'When should I drink a protein shake?',
      answer:
        'The idea of a critical 30-minute window after training is not as strongly supported as it once was. Current thinking is that total daily protein matters far more than timing. Drinking one after training is still a practical habit.',
    },
    {
      question: 'How many scoops of protein powder a day?',
      answer:
        'It depends on your daily protein target and how much you get from meals. For most people, 1-2 scoops a day cover the gap left by food. Think of protein powder as a supplement to meals, not the main source.',
    },
    {
      question: 'Is protein powder bad for your kidneys?',
      answer:
        'There is no strong evidence that a reasonable protein intake causes kidney damage in healthy people. It is different with an existing kidney condition; in that case protein intake should be discussed with a doctor.',
    },
    {
      question: 'Why does protein powder make me bloated?',
      answer:
        'In some people whey concentrate causes bloating or digestive discomfort because of its lactose. If you are lactose sensitive, isolate or plant proteins are worth considering; some products also add digestive enzymes to reduce this.',
    },
    {
      question: 'Should I mix it with milk or water?',
      answer:
        'Either works; the difference is calories and texture. With water you get only the powder\'s calories. With milk the shake is creamier and adds calories and protein, useful if you are trying to gain weight and something to count if you track calories.',
    },
    {
      question: 'Why do protein powder prices vary so much?',
      answer:
        'The protein source (concentrate, isolate, hydrolyzed, plant), tub size, flavor and brand positioning set the price. Bigger tubs usually cost less per serving, so the cheapest tub and the cheapest serving are often not the same product.',
    },
    {
      question: 'Should I take it on rest days too?',
      answer:
        'Yes, if you can\'t reach your daily protein target with food. Muscles don\'t only repair on training days; protein needs continue every day.',
    },
  ],

  creatine: [
    {
      question: 'What does creatine do?',
      answer:
        'Creatine helps performance in short, high-intensity efforts such as lifting and sprinting. It plays a role in energy production in muscle cells and is one of the most researched sports supplements.',
    },
    {
      question: 'How much creatine should I take a day?',
      answer:
        'The usual intake is 3-5 grams a day, and most products come with a 5-gram scoop. More doesn\'t speed up saturation; the excess is excreted. Consistency matters more than the amount.',
    },
    {
      question: 'How long does creatine take to work?',
      answer:
        'Creatine doesn\'t act instantly. Muscle stores need to reach saturation, which takes about 3-4 weeks of daily use, so expecting results after a day or two is misleading.',
    },
    {
      question: 'Does creatine make you retain water?',
      answer:
        'Creatine can increase water held in muscle cells, which may show as a few pounds on the scale. That weight is not fat but water inside the muscle. Drinking enough water while taking it is a common recommendation.',
    },
    {
      question: 'Does creatine cause hair loss?',
      answer:
        'This claim circulates widely. It traces back to a single small study that saw a hormone change; hair loss itself was not measured. There is no strong evidence that creatine causes hair loss, though there isn\'t enough research for a firm verdict either.',
    },
    {
      question: 'Do I need a loading phase?',
      answer:
        'No. Loading (a higher amount split into doses during the first week) reaches saturation faster. Starting directly at 3-5 grams gets to the same result a few weeks later. Some people get an upset stomach while loading.',
    },
    {
      question: 'Should I cycle off creatine?',
      answer:
        'There is no strong evidence that regular breaks are needed. When you stop, muscle creatine levels return to normal over time, and saturation starts again when you resume.',
    },
    {
      question: 'Can I take creatine with protein powder?',
      answer:
        'Yes. They work through entirely different mechanisms and don\'t interfere with each other\'s absorption. There is no known downside to mixing them in the same shake.',
    },
    {
      question: 'Creatine monohydrate or another form?',
      answer:
        'Creatine monohydrate is the most researched and usually the cheapest form. There is no strong evidence that other forms (HCl, ethyl ester) are clearly better.',
    },
  ],

  'amino-acids': [
    {
      question: 'What is the difference between BCAA and EAA?',
      answer:
        'EAAs (essential amino acids) are all nine amino acids the body can\'t make. BCAAs are three of them (leucine, isoleucine, valine). So BCAAs are a subset of EAAs.',
    },
    {
      question: 'Do I need BCAAs if I take protein powder?',
      answer:
        'Usually not. Whey protein is already rich in BCAAs; for someone meeting their daily protein target there is no strong evidence that extra BCAAs help. This is the most debated point about BCAA supplements.',
    },
    {
      question: 'What does glutamine do?',
      answer:
        'Glutamine is one of the most abundant amino acids in the body, which normally makes enough of it. Evidence that supplementing it improves performance in athletes is limited.',
    },
    {
      question: 'When should I take amino acids?',
      answer:
        'There is no strong evidence that timing makes a clear difference. Taking them during or around training is common, but total daily protein and amino acid intake matters much more.',
    },
    {
      question: 'What are arginine and citrulline used for?',
      answer:
        'Both support nitric oxide production, which is linked to blood flow, so they are associated with the "pump" during training. Citrulline is widely considered better absorbed than arginine for this purpose.',
    },
    {
      question: 'Do amino acid supplements build muscle?',
      answer:
        'No supplement builds muscle on its own. Muscle growth depends on training, enough total protein and rest; amino acid supplements can complement that picture, not replace it.',
    },
    {
      question: 'Can I take amino acids on an empty stomach?',
      answer:
        'Yes; they absorb quickly and rarely upset the stomach. It varies by person, though. If you feel discomfort, try them with a light snack.',
    },
    {
      question: 'EAA powder or capsules?',
      answer:
        'The active ingredient is the same; the difference is convenience and cost. Powders are usually cheaper per gram and easy to dose; capsules are portable but need many pills for the same amount.',
    },
  ],

  'pre-workout': [
    {
      question: 'What does pre-workout do?',
      answer:
        'It is used to support energy, focus and the feeling of performance before training. Most of the effect comes from caffeine; many also contain ingredients such as citrulline, associated with the "pump".',
    },
    {
      question: 'How long before a workout should I take it?',
      answer:
        'The usual advice is 20-30 minutes before. Caffeine levels in the blood start rising in that time. Taken too early, the peak can pass before the workout ends; too late, you won\'t feel anything until after your warm-up.',
    },
    {
      question: 'Why does pre-workout make me tingle?',
      answer:
        'The tingling on the skin usually comes from beta-alanine. It is a harmless effect whose intensity varies by person. It doesn\'t mean the product is "working"; it is simply a reaction to that ingredient.',
    },
    {
      question: 'Can I take pre-workout every day?',
      answer:
        'Regular high doses of caffeine build tolerance over time. Rather than raising the dose to feel the same effect, saving pre-workout for the hard sessions where you need it is more sustainable.',
    },
    {
      question: 'Can I take pre-workout for an evening workout?',
      answer:
        'Caffeine takes hours to clear the body, and a caffeinated product late in the day can noticeably affect sleep. Stimulant-free (caffeine-free) pre-workouts exist for late training.',
    },
    {
      question: 'What are the side effects of pre-workout?',
      answer:
        'At high caffeine doses, a racing heart, jitters and trouble falling asleep can occur, and coffee or tea during the day adds to the total. The FDA cites 400 mg of caffeine a day as generally safe for healthy adults. People with heart or blood pressure conditions should talk to a doctor.',
    },
    {
      question: 'Creatine or pre-workout?',
      answer:
        'They do different jobs and aren\'t alternatives. Creatine supports performance over time with daily use; a pre-workout gives an in-the-moment boost for that session. Some pre-workouts already contain creatine, so check the label if you take both.',
    },
    {
      question: 'Can I take half a scoop?',
      answer:
        'Yes, and it is common advice if you are new to a product. Caffeine per serving ranges from about 150 mg to over 300 mg, so "one scoop" doesn\'t mean the same thing in every product.',
    },
  ],

  hydration: [
    {
      question: 'Do I need an electrolyte drink?',
      answer:
        'For short, moderate workouts, water and normal meals are usually enough. Electrolytes matter more for long sessions, hot weather, heavy sweating or a low-carb diet.',
    },
    {
      question: 'How much sodium should an electrolyte mix have?',
      answer:
        'It depends on how much you sweat. Everyday mixes may have a few hundred milligrams per serving; endurance products can exceed 1,000 mg. Match it to your activity instead of assuming more is better.',
    },
    {
      question: 'Are sugar-free electrolyte mixes as good?',
      answer:
        'For hydration during everyday activity, yes. Sugar helps fluid absorb faster and supplies energy during long efforts, so it has a purpose in endurance sports, but it isn\'t needed for most daily use.',
    },
    {
      question: 'Can I drink electrolytes every day?',
      answer:
        'Many people do. If you are on a sodium-restricted diet or have high blood pressure, kidney or heart conditions, talk to a doctor before using high-sodium products regularly.',
    },
    {
      question: 'Electrolyte powder or tablets?',
      answer:
        'The minerals are the same; the difference is convenience and dose. Powders often carry more per serving and can include carbohydrate; tablets are easy to carry and usually lower in sodium.',
    },
  ],

  'mass-gainers': [
    {
      question: 'What is a mass gainer, and how is it different from protein powder?',
      answer:
        'A gainer contains a lot of carbohydrate alongside protein, with many calories per serving. Protein powder mainly covers a protein gap, while a gainer is designed for people who struggle to reach their daily calorie target.',
    },
    {
      question: 'Who is a mass gainer for?',
      answer:
        'It can be a practical option for people who find it hard to eat enough and are trying to gain weight. For someone already in a calorie surplus it isn\'t needed; regular protein powder and food offer more control.',
    },
    {
      question: 'Will a mass gainer make me fat?',
      answer:
        'A gainer is still calories. Eat above your daily needs and part of the weight gained is stored as fat. How much becomes muscle versus fat depends on your training and the size of the surplus.',
    },
    {
      question: 'How do I use a mass gainer?',
      answer:
        'Servings are usually large (over 100 grams in some products). If a full serving at once is too much, splitting it is common. Treat it as an addition to meals, not a replacement.',
    },
    {
      question: 'What can I use instead of a gainer?',
      answer:
        'High-calorie shakes made with oats, milk, bananas and peanut butter can do a similar job. A gainer\'s advantage is convenience, not that it is the only nutritious option.',
    },
    {
      question: 'Why do mass gainer prices vary?',
      answer:
        'Tub size, the protein-to-carb ratio and the protein source affect the price. Gainer tubs are large, so the price per pound can look low; compare cost per serving instead.',
    },
    {
      question: 'When should I drink a mass gainer?',
      answer:
        'Timing isn\'t critical; total daily calories are. After training or between meals are practical times to add calories without spoiling your appetite.',
    },
  ],

  vitamins: [
    {
      question: 'Does everyone need a vitamin supplement?',
      answer:
        'No. With a balanced, varied diet you can get most vitamins from food. Supplements make sense for a specific deficiency or an increased need, which is best identified with a blood test rather than guessed.',
    },
    {
      question: 'Multivitamin or single vitamins?',
      answer:
        'A multivitamin offers broad but low-dose coverage. For a specific deficiency (vitamin D, for example), a single, appropriately dosed product usually makes more sense. "A little of everything" isn\'t always the best answer.',
    },
    {
      question: 'When and how should I take vitamin D?',
      answer:
        'Vitamin D is fat-soluble, so taking it with a meal that contains fat is commonly recommended for absorption. The right dose varies by person; high doses need a doctor\'s supervision.',
    },
    {
      question: 'What does magnesium do?',
      answer:
        'Magnesium plays a role in muscle and nerve function and energy metabolism. It is popular among athletes for cramps and sleep quality; its forms (citrate, glycinate) differ in absorption and how they sit in the stomach.',
    },
    {
      question: 'When do I need a zinc supplement?',
      answer:
        'Zinc is linked to immune function and hormone balance. Long-term high doses can interfere with copper absorption, so talk to a health professional before long-term use.',
    },
    {
      question: 'When is omega-3 used?',
      answer:
        'It is used to cover omega-3 intake for people who eat little fish. Products differ a lot in EPA and DHA content; compare EPA/DHA per serving rather than the number of capsules.',
    },
    {
      question: 'Should vitamins be taken on an empty stomach?',
      answer:
        'Fat-soluble vitamins (A, D, E, K) absorb better with a meal that has fat. Water-soluble ones can be taken on an empty stomach but may upset it in some people; taking them with food usually solves that.',
    },
  ],

  'fat-burners': [
    {
      question: 'Do fat burner supplements really work?',
      answer:
        'Most contain ingredients said to slightly raise metabolism or curb appetite (mostly caffeine and plant extracts). Their effects are generally small and don\'t replace a calorie deficit. The claims in product names are more marketing than measured effect.',
    },
    {
      question: 'Can I lose weight without a fat burner?',
      answer:
        'Yes. Weight loss rests on a calorie deficit; no supplement produces fat loss without it. At best, supplements offer a small addition.',
    },
    {
      question: 'What does "thermogenic" mean?',
      answer:
        'It describes products said to raise body heat and therefore energy expenditure slightly. That increase is usually a small share of total daily calories burned.',
    },
    {
      question: 'Does L-carnitine help you lose weight?',
      answer:
        'Not on its own. L-carnitine helps move fatty acids into cells to be used for energy, but the body already makes enough, and evidence that supplementing it meaningfully increases fat loss is limited.',
    },
    {
      question: 'What is CLA?',
      answer:
        'CLA (conjugated linoleic acid) is a fatty acid found naturally in some animal foods. Studies on body composition have shown mixed results; expecting a large, reliable effect isn\'t realistic.',
    },
    {
      question: 'What are the side effects of fat burners?',
      answer:
        'Most contain a lot of caffeine, so a racing heart, jitters, poor sleep and raised blood pressure can occur. People with heart or blood pressure conditions or caffeine sensitivity shouldn\'t use them without talking to a doctor.',
    },
    {
      question: 'Can I take a fat burner with a pre-workout?',
      answer:
        'Be careful: both can be high in caffeine, and together the total climbs fast. Compare the labels and add up the caffeine.',
    },
  ],

  'protein-snacks': [
    {
      question: 'Are protein bars actually healthy?',
      answer:
        'It depends. Some bars are high in protein with reasonable sugar; others aren\'t very different from a candy bar nutritionally. Look at protein, sugar and total calories together on the label.',
    },
    {
      question: 'Can a protein bar replace a meal?',
      answer:
        'Usually not. Bars are a convenient snack but rarely match the fiber, micronutrients and fullness of a balanced meal. When you are short on time, they are a reasonable fallback.',
    },
    {
      question: 'Can protein bars make you gain weight?',
      answer:
        'Yes, if they push you above your calorie target, as with any food. Some bars have more calories than expected, so check the real calorie count rather than the "healthy" label.',
    },
    {
      question: 'Is a "no sugar added" snack really sugar-free?',
      answer:
        '"No sugar added" doesn\'t mean no carbohydrate; sweeteners or natural sugars may still be present. The total carbohydrate and sugar lines on the label are the reliable guide.',
    },
    {
      question: 'Can I eat a protein bar before a workout?',
      answer:
        'Yes, but bars high in fat and fiber can sit heavy. Close to training, a lighter, easier-to-digest option is more comfortable for most people.',
    },
  ],
};
