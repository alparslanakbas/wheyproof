// Readable names for the category slugs, shared by the footer, the nav
// dropdown and the category pages. The slugs must match the backend's
// ProductAttributeParser exactly, or a category page finds no products.
export const CATEGORY_LABELS: Record<string, string> = {
  'protein-powder': 'Protein Powder',
  creatine: 'Creatine',
  'amino-acids': 'Amino Acids',
  'pre-workout': 'Pre-Workout',
  hydration: 'Hydration & Electrolytes',
  'fat-burners': 'Fat Burners',
  'mass-gainers': 'Mass Gainers',
  vitamins: 'Vitamins & Minerals',
  'protein-snacks': 'Protein Snacks',
};

// Each category page opens with its own short introduction rather than a
// template sentence, to avoid thin content. Used by /category/:slug and the
// /categories index. Wording stays descriptive: supplement claims are
// regulated (FTC/FDA), so no promises about results.
export const CATEGORY_INTROS: Record<string, string> = {
  'protein-powder':
    'Protein powder is the most widely used sports supplement, an easy way to reach a daily protein target. Whey is absorbed quickly, casein more slowly, and plant blends suit dairy-free diets.',
  creatine:
    'Creatine is one of the most researched supplements for strength and power training. It is usually sold as creatine monohydrate and taken daily rather than only before a workout.',
  'amino-acids':
    'Amino acid supplements such as EAAs, BCAAs, glutamine and citrulline are taken around training, often as a flavored drink between meals or during a session.',
  'pre-workout':
    'Pre-workout formulas typically combine caffeine with ingredients such as beta-alanine and citrulline. Caffeine content varies widely between products, so check the label.',
  hydration:
    'Hydration and electrolyte mixes add sodium, potassium and magnesium to water, for long sessions, hot weather or low-carb diets.',
  'fat-burners':
    'Fat burners are thermogenic formulas, usually built around caffeine and plant extracts, used alongside a diet and training plan. They are not a substitute for either.',
  'mass-gainers':
    'Mass gainers pack protein with a large dose of carbohydrates and calories for people who struggle to eat enough to gain weight.',
  vitamins:
    'Vitamins and minerals cover everyday nutrition gaps, from multivitamins and fish oil to magnesium, zinc and vitamin D.',
  'protein-snacks':
    'Protein bars, cookies and chips are a convenient way to add protein between meals. Compare them per gram of protein, not per package.',
};
