// Long-form guide content on category pages. Competing sites run category
// pages as structured 2,500-4,800 word guides with H2/H3 sections, where ours
// were a product table and a short intro (CATEGORY_INTROS). A category
// without an entry here simply doesn't show the section.
//
// Tone: honest, no hype, no firm medical claims, uncertainty stated plainly.
// Supplement claims are regulated in the US (FTC/FDA): describe what research
// suggests, never promise results.

import { SITE_NAME } from './site-identity';

export interface CategoryGuideSection {
  heading: string;
  paragraphs: string[];
  table?: { headers: string[]; rows: string[][] };
}

export interface CategoryGuide {
  // A one-paragraph definition at the top of the page, marked up as a
  // "zero-click answer" for search and AI engines to quote (matches the
  // schema.org Speakable markup, see category-page.ts).
  zeroClickAnswer: string;
  sections: CategoryGuideSection[];
  // A real link to the related guide (internal linking) rather than
  // "see our guide" in plain, unclickable text.
  relatedArticleSlug: string;
  relatedArticleTitle: string;
}

export const CATEGORY_GUIDES: Partial<Record<string, CategoryGuide>> = {
  'protein-powder': {
    zeroClickAnswer:
      'Comparing protein powders means looking past the tub size to objective numbers: protein per serving, ' +
      'cost per gram of protein, protein type (whey concentrate, isolate, casein, plant) and purity. Checking how ' +
      'many grams of protein a serving really has, and what that costs, avoids the most common mistake: assuming ' +
      'the biggest tub is the best deal.',
    sections: [
      {
        heading: 'Types of Protein Powder and How They Differ',
        paragraphs: [
          'Most protein powders are dairy-based: whey and casein. Whey concentrate (WPC) is the most common and ' +
            'usually the cheapest, typically 70-80% protein with small amounts of fat and lactose. Whey isolate ' +
            '(WPI) goes through an extra filtration step, reaching 85-95% protein with almost no lactose, which is ' +
            'why people with lactose sensitivity often choose it.',
          'Casein also comes from milk but digests much more slowly (whey in minutes, casein over hours), so it ' +
            'is usually taken when a longer, steady supply of protein is wanted, such as before bed, rather than ' +
            'for quick post-workout recovery.',
          'Plant proteins (pea, rice, hemp, soy or blends) contain no dairy and are the option for vegan or ' +
            'lactose-intolerant users. A single plant source often has an incomplete amino acid profile (rice is ' +
            'low in lysine, pea in methionine), so good plant products usually blend several sources.',
        ],
        table: {
          headers: ['Type', 'Protein Content', 'Lactose', 'Digestion', 'Best For'],
          rows: [
            ['Whey Concentrate (WPC)', '70-80%', 'Low-moderate', 'Fast', 'Everyday use, lower price'],
            ['Whey Isolate (WPI)', '85-95%', 'Almost none', 'Fast', 'Lactose sensitivity, low-calorie goals'],
            ['Casein', '80-90%', 'Low', 'Slow', 'Before bed, long gaps between meals'],
            ['Plant (blend)', '70-80%', 'None', 'Moderate', 'Vegan or lactose intolerance'],
          ],
        },
      },
      {
        heading: 'What to Look for When Comparing Prices',
        paragraphs: [
          'Comparing two products by tub price is misleading. A 5 lb tub may look "cheaper" than a 2 lb one, but ' +
            'if the protein per serving differs, the real cost can be the other way around. The right unit is the ' +
            'cost of protein: tub price ÷ (servings per tub × grams of protein per serving).',
          `${SITE_NAME} runs this calculation automatically when the brand publishes its serving size and protein ` +
            'per serving, and shows it as the price per serving. When the brand does not publish it, we show no ' +
            'estimate; the field stays empty.',
        ],
      },
      {
        heading: 'What Are Bioavailability and Amino Acid Profile?',
        paragraphs: [
          'Bioavailability describes how much of the protein you take in the body can actually use; the total ' +
            'grams on the label don\'t tell the whole story. Whey is generally considered highly bioavailable ' +
            'because it is absorbed quickly and is rich in leucine, the branched-chain amino acid most associated ' +
            'with triggering muscle protein synthesis.',
          'The International Society of Sports Nutrition (ISSN) suggests considering leucine per serving (a ' +
            'threshold of roughly 2-3 g) when comparing proteins. It is a closer answer to "how much of this ' +
            'protein does the job" than grams of protein alone.',
        ],
      },
      {
        heading: 'Why Third-Party Testing Matters',
        paragraphs: [
          'In the US, the FDA does not approve dietary supplements before they are sold, and label claims can ' +
            'differ from what is in the tub. One known practice is "amino spiking": adding cheap free amino acids ' +
            'to inflate the protein number. Independent programs such as NSF Certified for Sport or Informed Sport ' +
            'mean a third party has tested that the product contains what the label says and no banned substances.',
          `${SITE_NAME} doesn't track these certifications as a separate field yet. When evaluating a product, ` +
            'checking whether the brand\'s own product page mentions one adds an extra layer of confidence.',
        ],
      },
      {
        heading: 'Concentrate or Isolate: Which Should You Choose?',
        paragraphs: [
          'Both are good options; the right answer depends on your needs. If budget matters most and you have no ' +
            'lactose sensitivity, whey concentrate usually offers the better price per gram. If you are sensitive ' +
            'to lactose, cutting calories (isolate has less fat and carbohydrate) or want higher protein purity, ' +
            'isolate can be worth the difference.',
        ],
      },
    ],
    relatedArticleSlug: 'how-to-choose-whey-protein',
    relatedArticleTitle: 'How to Choose a Whey Protein',
  },
  creatine: {
    zeroClickAnswer:
      'The two points most often confused when comparing creatine are the real difference between forms and ' +
      'whether a loading phase is needed. Creatine monohydrate is the most researched form with the strongest ' +
      'evidence; there is no strong evidence that pricier alternatives such as creatine HCl or buffered creatine ' +
      'work meaningfully better.',
    sections: [
      {
        heading: 'The Real Differences Between Creatine Forms',
        paragraphs: [
          'Creatine monohydrate is the oldest, most studied and usually the cheapest form. Micronized ' +
            'monohydrate is the same molecule milled to a finer particle size: it mixes more easily in water, but ' +
            'it is not a "stronger" version.',
          'Forms such as creatine HCl and buffered creatine (Kre-Alkalyn) are marketed as causing less water ' +
            'retention or less stomach upset, but far fewer large, independent studies support those claims than ' +
            'support monohydrate. It is worth knowing the price difference isn\'t backed by a proven advantage.',
          'Creapure is not a brand of supplement but a trademarked, high-purity creatine monohydrate made in ' +
            'Germany. Many brands state they use it, which can be read as a purity and quality assurance.',
        ],
      },
      {
        heading: 'Is a Loading Phase Necessary?',
        paragraphs: [
          'The traditional protocol was a "loading" phase of 20 g a day (split into four doses) for 5-7 days, ' +
            'then 3-5 g a day to maintain. Loading only fills muscle creatine stores FASTER. Skip it and start at ' +
            '3-5 g a day, and you reach the same saturation in about 3-4 weeks; the end result is the same.',
          'So loading is optional, a matter of speed. People who want to avoid stomach discomfort can start ' +
            'directly with the maintenance dose.',
        ],
      },
      {
        heading: 'Why Water Retention and Weight Gain Happen',
        paragraphs: [
          'Creatine draws water into muscle cells (intracellular hydration). That is not "bloating" under the ' +
            'skin but water held inside the muscle, which can make it look fuller. A gain of 2-4 lb in the first ' +
            'week or two usually comes from this water, not fat.',
          'The effect is reversible and fades within a few weeks of stopping creatine.',
        ],
      },
      {
        heading: 'Purity and Grams When Comparing Prices',
        paragraphs: [
          'Comparing creatine prices is relatively simple because the active ingredient is a single compound. ' +
            'The thing to check is whether the product is pure creatine monohydrate or a "creatine complex" ' +
            'diluted with other, usually cheaper, ingredients. When a product doesn\'t say "monohydrate" on the ' +
            'label, keep that distinction in mind.',
        ],
      },
      {
        heading: 'Who Should Be Careful?',
        paragraphs: [
          'People with impaired kidney function should talk to a doctor before taking creatine. There is a broad ' +
            'body of research on its safety with healthy kidneys, but that can\'t be generalized to an existing ' +
            'kidney condition.',
        ],
      },
    ],
    relatedArticleSlug: 'creatine-what-to-know',
    relatedArticleTitle: 'Creatine: What to Know Before You Buy',
  },
  'pre-workout': {
    zeroClickAnswer:
      'When comparing pre-workouts, the key is not the number of ingredients on the label but the caffeine ' +
      'content and whether the few ingredients with research behind them (beta-alanine, citrulline, creatine) ' +
      'appear at effective doses. Products listing many ingredients in a "proprietary blend" without doses ' +
      'usually contain small amounts of each.',
    sections: [
      {
        heading: 'The Core Ingredients in a Pre-Workout',
        paragraphs: [
          'Caffeine is the most common ingredient with the clearest evidence, associated with alertness, lower ' +
            'perceived effort and short-term performance. Doses per serving vary widely (roughly 150-400 mg), so ' +
            'it is the first number to check.',
          'Beta-alanine raises carnosine levels in muscle and can help delay fatigue in high-intensity efforts ' +
            'lasting 1-4 minutes. Effective intake is usually considered 3.2-6.4 g a day, and it works by building ' +
            'up over time, not from a single dose.',
          'Citrulline (including citrulline malate) supports blood flow, adds to the "pump" feeling and in some ' +
            'studies shows a small contribution to exercise capacity. Effective doses are usually around 6-8 g of ' +
            'citrulline malate; many budget products use far less.',
        ],
      },
      {
        heading: 'Why Does It Make You Tingle?',
        paragraphs: [
          'The tingling in the face or hands after beta-alanine (paresthesia) is harmless but can be ' +
            'uncomfortable; it comes from beta-alanine briefly stimulating nerve endings. Splitting the dose can ' +
            'reduce it. It is a natural effect of the ingredient, not a sign of product quality.',
        ],
      },
      {
        heading: 'Caffeine Sensitivity and Dose Range',
        paragraphs: [
          'For caffeine-sensitive people, under 150 mg can count as a low dose, while experienced users may ' +
            'prefer 300 mg or more. The FDA cites 400 mg a day as an amount not generally associated with negative ' +
            'effects in healthy adults, and that total includes coffee, tea and energy drinks. Think of the ' +
            'pre-workout dose as part of your daily total, not on its own.',
          'For evening workouts, a high-caffeine pre-workout can affect sleep; caffeine\'s half-life averages ' +
            'around 5 hours.',
        ],
      },
      {
        heading: 'Pre-Workouts That Contain Creatine',
        paragraphs: [
          'Some pre-workouts add creatine. That is fine in itself, but creatine works through regular daily use ' +
            '(see the creatine guide). If you only take the pre-workout on training days, your creatine intake is ' +
            'irregular; a separate creatine supplement gives more consistent results.',
        ],
      },
      {
        heading: 'How to Compare Price per Serving',
        paragraphs: [
          'Here too, look at cost per serving rather than tub price, and go one step further: divide it by the ' +
            'caffeine, beta-alanine and citrulline per serving to get a "cost per effective dose". It reveals the ' +
            'real difference between two products that look alike.',
        ],
      },
    ],
    relatedArticleSlug: 'how-to-choose-a-pre-workout',
    relatedArticleTitle: 'How to Choose a Pre-Workout',
  },
  'amino-acids': {
    zeroClickAnswer:
      'The first distinction when comparing amino acid supplements is BCAA (three branched-chain amino acids) ' +
      'versus EAA (all nine essential amino acids). EAAs include every amino acid in a BCAA, so they are ' +
      'generally considered more complete for muscle protein synthesis, but for someone who already eats enough ' +
      'protein (whey, meat, eggs), either adds little.',
    sections: [
      {
        heading: 'BCAA or EAA? The Core Difference',
        paragraphs: [
          'BCAAs (branched-chain amino acids) are leucine, isoleucine and valine, focused on leucine, the main ' +
            'signal for muscle protein synthesis. EAAs (essential amino acids) are all nine amino acids the body ' +
            'can\'t make itself, and the three BCAAs are among them.',
          'The leucine signal alone isn\'t enough to build muscle protein; the other eight essential amino acids ' +
            'are needed too. That is why sports nutrition research in recent years has favored EAAs as the more ' +
            'complete option.',
        ],
      },
      {
        heading: 'Do You Need Them If You Already Eat Enough Protein?',
        paragraphs: [
          'If you already meet your daily protein needs from complete sources (whey, meat, eggs, dairy, soy), ' +
            'extra BCAAs or EAAs add limited benefit for muscle growth, since complete proteins already contain ' +
            'every essential amino acid. They make the most sense when training fasted or when reaching a daily ' +
            'protein target is hard.',
        ],
      },
      {
        heading: 'Amino Acid Profile and Ratios',
        paragraphs: [
          'Ratios such as "2:1:1" or "4:1:1" on BCAA products describe leucine:isoleucine:valine. A higher ' +
            'leucine ratio usually means a stronger synthesis signal, but it matters less than the total amount. ' +
            'Compare grams per serving, not just the ratio.',
        ],
      },
      {
        heading: 'When to Take Them',
        paragraphs: [
          'BCAAs and EAAs are usually taken during or around training; mixed in water, they digest quickly. On ' +
            'long fasted cardio sessions they are sometimes used to limit muscle breakdown for energy.',
        ],
      },
      {
        heading: 'Glutamine and Other Amino Acids',
        paragraphs: [
          'Glutamine is linked to immune and gut health, but its direct effect on muscle growth is not as well ' +
            'supported as BCAAs or EAAs; some products add it. Citrulline is an amino acid too, but its main effect ' +
            'is on blood flow and the pump, a different mechanism from muscle protein synthesis.',
        ],
      },
      {
        heading: 'What to Look for When Comparing Prices',
        paragraphs: [
          'The same rule applies: compare total grams of amino acids and leucine per serving, not tub price. ' +
            'Some products list many amino acids at low doses under an "amino complex" name, which is usually ' +
            'label length for marketing rather than effective doses.',
        ],
      },
    ],
    relatedArticleSlug: 'bcaa-vs-eaa',
    relatedArticleTitle: 'BCAA vs EAA: An Amino Acid Guide',
  },
  hydration: {
    zeroClickAnswer:
      'Hydration and electrolyte mixes add sodium, potassium and often magnesium to water. When comparing them, ' +
      'the key numbers are sodium per serving (it varies from under 100 mg to over 1,000 mg), sugar content and ' +
      'cost per serving. The right product depends on how much and how long you sweat, not on the length of the ' +
      'ingredient list.',
    sections: [
      {
        heading: 'What Electrolytes Do',
        paragraphs: [
          'Electrolytes are minerals that carry an electrical charge and help regulate fluid balance, nerve ' +
            'signals and muscle function. Sodium is the one lost in the largest amounts in sweat, followed by ' +
            'potassium, with smaller amounts of magnesium and calcium.',
          'For most people doing short, moderate workouts, water and normal meals replace what is lost. ' +
            'Electrolyte products matter more for long sessions, hot conditions, heavy sweaters and low-carb diets.',
        ],
      },
      {
        heading: 'Sodium Is the Number to Compare',
        paragraphs: [
          'Products differ most in sodium: some are closer to flavored water, others are built for endurance ' +
            'athletes. A heavy sweater in the heat may lose well over 1,000 mg of sodium per hour; someone lifting ' +
            'for 45 minutes loses far less. Match the sodium to your situation rather than assuming more is better.',
        ],
      },
      {
        heading: 'Sugar or No Sugar?',
        paragraphs: [
          'Some mixes include sugar, which speeds fluid absorption and supplies energy during long efforts. For ' +
            'everyday use or low-calorie goals, sugar-free versions sweetened with stevia or sucralose are common. ' +
            'Neither is better in general; it depends on the activity.',
        ],
      },
      {
        heading: 'Who Should Be Careful?',
        paragraphs: [
          'People on a sodium-restricted diet, with high blood pressure, or with kidney or heart conditions ' +
            'should talk to a doctor before using high-sodium products regularly.',
        ],
      },
      {
        heading: 'What to Look for When Comparing Prices',
        paragraphs: [
          'Compare cost per serving and sodium per serving together. A cheap tub with little sodium may cost ' +
            'more per useful dose than a pricier, more concentrated one.',
        ],
      },
    ],
    relatedArticleSlug: 'electrolytes-explained',
    relatedArticleTitle: 'Electrolytes Explained',
  },
  'mass-gainers': {
    zeroClickAnswer:
      'When comparing mass gainers, the key criteria are calories per serving and the split of carbohydrate, ' +
      'protein and fat. Unlike standard protein powders, gainers are designed to make a high calorie intake ' +
      'easier, so they shouldn\'t be compared the same way (by protein alone).',
    sections: [
      {
        heading: 'What Is a Gainer, and Who Is It For?',
        paragraphs: [
          'A gainer contains far more carbohydrate than a standard protein powder (usually maltodextrin or ' +
            'another fast-digesting source), with anywhere from about 300 to over 1,200 calories per serving. It ' +
            'is a practical option for people who struggle to eat enough to reach their calorie target and want ' +
            'to gain weight.',
          'For someone who gains weight easily or tends to add fat, a gainer usually isn\'t necessary; a regular ' +
            'protein powder plus adequate meals is enough.',
        ],
      },
      {
        heading: 'Calorie Density and Macro Split',
        paragraphs: [
          'Calorie density varies widely: some servings are around 300 calories (closer to a high-protein ' +
            'snack), others exceed 1,000. Compare them by which fits your intended daily surplus, not by tub price.',
        ],
      },
      {
        heading: 'Watch the Sugar and Maltodextrin',
        paragraphs: [
          'In cheaper gainers, most of the calories often come from simple sugar or maltodextrin, which provide ' +
            'quick energy but can spike blood sugar. Better products diversify carbohydrate sources (oats, sweet ' +
            'potato flour). The carbohydrate source on the label says more than total calories alone.',
        ],
      },
      {
        heading: 'How It Differs From Protein Powder',
        paragraphs: [
          'A standard whey protein is usually 100-150 calories per serving and low in carbohydrate; a gainer ' +
            'deliberately raises calorie density. For someone who only wants to cover protein needs without ' +
            'gaining weight, a gainer is the wrong choice: the two serve different goals.',
        ],
      },
      {
        heading: 'Cost per Calorie When Comparing Prices',
        paragraphs: [
          'For gainers the most meaningful unit is usually cost per calorie, not just per gram of protein, since ' +
            'the product\'s main job is supplying calories. Two gainers at the same price can differ a lot if their ' +
            'calories per serving differ.',
        ],
      },
    ],
    relatedArticleSlug: 'how-to-use-a-mass-gainer',
    relatedArticleTitle: 'How to Use a Mass Gainer',
  },
  'fat-burners': {
    zeroClickAnswer:
      'The most important fact when comparing fat burners: no supplement causes fat loss without a calorie ' +
      'deficit and regular training. At best these products modestly support energy expenditure or appetite; ' +
      'they don\'t "melt fat". Ingredients such as L-carnitine and CLA are sold in this category too, with modest ' +
      'and inconsistent results in human studies.',
    sections: [
      {
        heading: 'Do Fat Burners Actually Burn Fat?',
        paragraphs: [
          'Thermogenic ingredients (caffeine, green tea extract) can produce a small, temporary rise in ' +
            'metabolic rate, but it is tiny next to a daily calorie deficit. Someone eating in a surplus won\'t ' +
            'lose fat with any fat burner; the realistic role is a marginal addition on top of the right diet and ' +
            'training.',
        ],
      },
      {
        heading: 'Common Thermogenic Ingredients',
        paragraphs: [
          'The most common are caffeine, green tea extract (EGCG), green coffee extract and capsaicin (chili ' +
            'pepper extract). They are associated with a slight increase in energy expenditure, but the effect ' +
            'varies by person and guarantees no large fat loss.',
        ],
      },
      {
        heading: 'L-Carnitine and CLA',
        paragraphs: [
          'L-carnitine carries fatty acids into the mitochondria, where they are used for energy. The body ' +
            'already makes enough, and only a limited share of supplemental L-carnitine reaches muscle, so its ' +
            'effect is more modest than marketing suggests. It comes as L-carnitine tartrate, acetyl-L-carnitine ' +
            '(ALCAR) and liquid shots; none has proven fat-burning effects.',
          'CLA (conjugated linoleic acid) is a fatty acid found naturally in meat and dairy, said to affect fat ' +
            'storage through a different mechanism. Human studies show mixed results: some a small difference, ' +
            'many none.',
        ],
      },
      {
        heading: 'Side Effects and Cautions',
        paragraphs: [
          'Most fat burners contain large amounts of caffeine and similar stimulants, which can cause a racing ' +
            'heart, sleeplessness and anxiety, especially on top of coffee during the day. People with heart or ' +
            'blood pressure conditions should talk to a doctor before using them.',
        ],
      },
      {
        heading: 'What to Look for When Comparing Prices',
        paragraphs: [
          'Compare caffeine and other active ingredients per serving, as with pre-workouts. Products that list ' +
            'many ingredients at low amounts may be little more than an unproven "proprietary blend".',
        ],
      },
    ],
    relatedArticleSlug: 'do-fat-burners-work',
    relatedArticleTitle: 'Do Fat Burners Actually Work?',
  },
  'protein-snacks': {
    zeroClickAnswer:
      'When comparing protein snacks, the "protein bar" label alone isn\'t a useful measure. The balance of ' +
      'sugar or sweeteners, real protein content and calories per serving decides whether a product is a better ' +
      'choice.',
    sections: [
      {
        heading: 'What Counts as a Protein Snack?',
        paragraphs: [
          'The category covers protein bars, cookies, chips and similar snacks. "Healthy" has no standard ' +
            'definition on a snack wrapper; the nutrition facts panel tells you more than the marketing copy.',
        ],
      },
      {
        heading: 'What to Check in a Protein Bar',
        paragraphs: [
          'Three numbers matter: protein per serving, total sugar and total calories. Some "protein bars" are ' +
            'actually low in protein and high in sugar; having "protein" in the name isn\'t a quality signal.',
        ],
      },
      {
        heading: 'Sugar Alcohols and Digestive Discomfort',
        paragraphs: [
          'Low-sugar bars often use sugar alcohols such as erythritol or maltitol. They affect blood sugar less ' +
            'but can cause bloating or discomfort in some people in larger amounts. That is individual tolerance, ' +
            'not product quality.',
        ],
      },
      {
        heading: 'How to Read the Macro Balance',
        paragraphs: [
          'Judge a snack by whether its protein, carbohydrate and fat split suits your goal (losing weight, ' +
            'gaining muscle, general nutrition) rather than by one number such as calories or protein alone.',
        ],
      },
      {
        heading: 'What to Look for When Comparing Prices',
        paragraphs: [
          'Serving sizes vary a lot between snacks. The cost per gram of protein reveals which product is really ' +
            'cheaper, more than the price per bar or box.',
        ],
      },
    ],
    relatedArticleSlug: 'how-to-choose-protein-bars',
    relatedArticleTitle: 'How to Choose Protein Bars',
  },
  vitamins: {
    zeroClickAnswer:
      'The key question when comparing vitamin and mineral supplements is whether you need to cover a specific ' +
      'deficiency (vitamin D, for example) or want general support. Multivitamins offer broad but low-dose ' +
      'coverage, while single vitamins address a targeted need at a higher dose.',
    sections: [
      {
        heading: 'Multivitamin or Single Vitamin?',
        paragraphs: [
          'Multivitamins contain many vitamins and minerals at low to moderate doses to help prevent general ' +
            'gaps; they don\'t target a specific deficiency. If a blood test shows one (vitamin D, iron, B12), a ' +
            'single, appropriately dosed supplement is usually more effective than the small amount in a ' +
            'multivitamin.',
        ],
      },
      {
        heading: 'Absorption and Forms',
        paragraphs: [
          'Some vitamins come in more than one chemical form with different absorption. For vitamin B12, for ' +
            'example, there is cyanocobalamin (common and cheap) and methylcobalamin (an active form). The form ' +
            'shows in the price, so ask not just "does it have B12" but "which form".',
        ],
      },
      {
        heading: 'Fat-Soluble and Water-Soluble Vitamins',
        paragraphs: [
          'Vitamins A, D, E and K are fat-soluble and can be stored in the body, so very high doses over long ' +
            'periods (especially A and D) carry a theoretical risk of buildup. B vitamins and vitamin C are ' +
            'water-soluble, and excess is mostly excreted. That is why "more is always better" is wrong.',
        ],
      },
      {
        heading: 'Needs of People Who Train',
        paragraphs: [
          'Vitamin D and magnesium shortfalls are relatively common in people who train regularly (indoor ' +
            'training, minerals lost in sweat), but generalizations don\'t replace a personal blood test, the most ' +
            'reliable way to know what you need.',
        ],
      },
      {
        heading: 'What to Look for When Comparing Prices',
        paragraphs: [
          'Compare the amount of each ingredient per serving against its Daily Value (%DV) on the Supplement ' +
            'Facts panel. Some cheap products list many vitamins but contain very little of each.',
        ],
      },
    ],
    relatedArticleSlug: 'how-to-choose-vitamins-and-minerals',
    relatedArticleTitle: 'How to Choose Vitamin and Mineral Supplements',
  },
};
