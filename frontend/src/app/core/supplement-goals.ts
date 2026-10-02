import { FaqItem } from './category-faqs';
import { MARKET } from './market';
import { PRICE_UNIT } from './unit-price';

// "Which supplement should I take?" — the single content source of the quiz
// and the goal pages.
//
// STRUCTURE: the quiz runs in the browser; the result is ONE SSR page PER GOAL
// (four pages). There is no page per answer combination: the other answers
// personalize the goal page through query parameters, and the canonical stays
// the clean URL.
//
// TONE: plain and hedged, no health claims (supplement claims are regulated by
// the FTC/FDA; the UK has its own rules). Every page also says what ISN'T
// needed; that is the supplement side of the site's "is this deal real?"
// question. The quiz asks no health questions, and every page carries a doctor
// note.

export const FINDER_PATH = '/which-supplement';

const IMPERIAL = MARKET.measurement === 'imperial';
const PROTEIN_TARGET = IMPERIAL
  ? 'Target: 0.7–1 g protein per lb of body weight'
  : 'Target: 1.6–2.2 g protein per kg of body weight';
const CREATINE_WATER = IMPERIAL ? '2–4 lb' : '1–2 kg';
const WEEKLY_GAIN = IMPERIAL ? '0.5–1 lb' : '0.25–0.5 kg';

/** Verdict level; also the order of the rows on the page. */
export type VerdictLevel = 'priority' | 'optional' | 'skip';

export const VERDICT_LABELS: Record<VerdictLevel, string> = {
  priority: 'Priority',
  optional: 'Optional',
  skip: 'Skip',
};

export interface GoalLink {
  label: string;
  path: string;
}

export interface GoalVerdict {
  level: VerdictLevel;
  name: string;
  reason: string;
  /** The commonly used amount; only where there is an established range. */
  dose?: string;
  link?: GoalLink;
  /** The id of this supplement's product list on the page, if it has one. */
  pickSectionId?: string;
}

/** A product list on a goal page (/api/value-picks). */
export interface GoalPickSection {
  id: string;
  category: string;
  /** Narrowing type of the endpoint (e.g. "gainer" in mass gainers). */
  type?: string;
  title: string;
  note?: string;
  /** Show the isolate/plant chips (protein powder). */
  proteinFilter?: boolean;
  allLink: GoalLink;
}

export type QuizBranch = 'protein' | 'endurance';

export interface SupplementGoal {
  slug: string;
  /** The quiz option and link text. */
  label: string;
  /** The quiz option's description. */
  description: string;
  icon: string;
  h1: string;
  metaTitle: string;
  metaDescription: string;
  /** The one-paragraph answer that opens the page. */
  answer: string;
  verdicts: GoalVerdict[];
  picks: GoalPickSection[];
  faqs: FaqItem[];
  quizBranch: QuizBranch;
}

const CREATINE_PICKS: GoalPickSection = {
  id: 'creatine',
  category: 'creatine',
  title: 'Creatine',
  allLink: { label: 'All creatine prices', path: '/category/creatine' },
};

const PROTEIN_PICKS: GoalPickSection = {
  id: 'protein-powder',
  category: 'protein-powder',
  title: 'Protein powder',
  proteinFilter: true,
  allLink: { label: 'All protein powder prices', path: '/category/protein-powder' },
};

const PROTEIN_CALCULATOR: GoalLink = { label: 'Protein calculator', path: '/calculators/protein' };
const CREATINE_DOSAGE: GoalLink = { label: 'Creatine dosage', path: '/calculators/creatine-dosage' };

export const SUPPLEMENT_GOALS: SupplementGoal[] = [
  {
    slug: 'build-muscle',
    label: 'Build muscle and strength',
    description: 'I lift weights and want to get stronger',
    icon: 'ph-barbell',
    h1: 'Which supplements help build muscle?',
    metaTitle: 'Which Supplements Help Build Muscle? | WheyProof',
    metaDescription:
      `Two supplements with strong evidence for muscle and strength: creatine and, if you need it, protein powder. What to skip, and the lowest prices per ${PRICE_UNIT.word} today.`,
    answer:
      'Muscle gain comes from training, enough calories and enough protein; supplements replace none of them. Two ' +
      "have strong evidence: creatine monohydrate, and protein powder if you can't reach your protein target from " +
      'food. The rest are optional or unnecessary.',
    verdicts: [
      {
        level: 'priority',
        name: 'Creatine monohydrate',
        reason:
          'The most researched supplement for strength and muscle gain. Monohydrate is all you need; taking it every ' +
          'day matters more than the form.',
        dose: '3–5 g a day',
        link: CREATINE_DOSAGE,
        pickSectionId: 'creatine',
      },
      {
        level: 'priority',
        name: 'Protein powder (if needed)',
        reason:
          'What counts for muscle is total daily protein. If food falls short, powder is the easiest way to close the ' +
          "gap; if it doesn't, powder adds nothing.",
        dose: PROTEIN_TARGET,
        link: PROTEIN_CALCULATOR,
        pickSectionId: 'protein-powder',
      },
      {
        level: 'optional',
        name: 'Caffeine or a pre-workout',
        reason:
          'Can lift training performance a little. The active ingredient in most pre-workouts is caffeine; skip it if ' +
          "you're sensitive to caffeine or train in the evening.",
        link: { label: 'How to choose a pre-workout', path: '/guides/how-to-choose-a-pre-workout' },
      },
      {
        level: 'skip',
        name: 'BCAAs',
        reason:
          "Haven't been shown to add muscle when daily protein is already enough; protein powder already contains them.",
        link: { label: 'BCAA vs EAA', path: '/guides/bcaa-vs-eaa' },
      },
      {
        level: 'skip',
        name: 'Testosterone boosters',
        reason:
          "There's no strong evidence that herbal boosters such as tribulus increase muscle or strength in healthy men.",
      },
    ],
    picks: [CREATINE_PICKS, PROTEIN_PICKS],
    faqs: [
      {
        question: 'Do I need supplements to build muscle?',
        answer:
          'No. Muscle gain comes from training, enough calories and protein. Supplements make those easier; apart ' +
          'from creatine, most have small effects.',
      },
      {
        question: 'Can I take creatine and protein powder together?',
        answer:
          'Yes. They do different jobs and work fine together. Taking creatine every day at the same dose matters more ' +
          'than when you take it.',
      },
      {
        question: 'What should a beginner start with?',
        answer:
          'Most early progress comes from training. If you want supplements, creatine monohydrate and, if food falls ' +
          'short on protein, protein powder are enough.',
      },
    ],
    quizBranch: 'protein',
  },
  {
    slug: 'gain-weight',
    label: 'Gain weight',
    description: 'I struggle to eat enough to put on weight',
    icon: 'ph-scales',
    h1: 'Which supplements help you gain weight?',
    metaTitle: 'Which Supplements Help You Gain Weight? | WheyProof',
    metaDescription:
      `Struggling to gain weight? When a mass gainer helps, when a plain carb powder is the cheaper route, and what to skip. Current prices per ${PRICE_UNIT.word}.`,
    answer:
      "What drives weight gain is a calorie surplus. If you can't eat enough, a mass gainer is convenient; mixing a " +
      'carbohydrate powder with protein powder is often cheaper. Creatine supports muscle gain.',
    verdicts: [
      {
        level: 'priority',
        name: 'Mass gainer or carb powder (if needed)',
        reason:
          "Closes the calorie gap when food alone isn't enough. A gainer is a ready-made mix; a carb powder (cream of " +
          'rice, maltodextrin) plus protein powder usually does the same for less.',
        link: { label: 'How to use a mass gainer', path: '/guides/how-to-use-a-mass-gainer' },
        pickSectionId: 'gainer',
      },
      {
        level: 'priority',
        name: 'Creatine monohydrate',
        reason: `Supports muscle gain. The water stored in muscle in the first weeks usually shows up as ${CREATINE_WATER} on the scale.`,
        dose: '3–5 g a day',
        link: CREATINE_DOSAGE,
        pickSectionId: 'creatine',
      },
      {
        level: 'optional',
        name: 'Protein powder',
        reason: "Adds protein to the surplus if you're not using a gainer or your protein target falls short.",
        link: PROTEIN_CALCULATOR,
      },
      {
        level: 'skip',
        name: 'BCAAs and EAAs',
        reason: "Don't close a calorie or protein gap; the same money buys more protein powder or food.",
      },
    ],
    picks: [
      {
        id: 'gainer',
        category: 'mass-gainers',
        type: 'gainer',
        title: 'Mass gainers',
        allLink: { label: 'All mass gainers', path: '/category/mass-gainers' },
      },
      {
        id: 'carbs',
        category: 'mass-gainers',
        type: 'carbs',
        title: 'Carb powders',
        note: 'Plain carbohydrate sources such as cream of rice and maltodextrin, mixed with protein powder.',
        allLink: { label: 'All mass gainers', path: '/category/mass-gainers' },
      },
      CREATINE_PICKS,
    ],
    faqs: [
      {
        question: 'Mass gainer or protein powder?',
        answer:
          "If you struggle to gain weight, what's usually missing is calories; a gainer gives calories and protein " +
          'together. If you already hit your protein target, a carb powder is the cheaper route.',
      },
      {
        question: 'Will a mass gainer make me fat?',
        answer:
          'A gainer is just calories. Calories beyond your needs are stored as fat whatever the source. Gaining about ' +
          `${WEEKLY_GAIN} a week is a sensible pace for most people.`,
      },
      {
        question: 'Does creatine help you gain weight?',
        answer:
          `Creatine supports muscle gain and adds ${CREATINE_WATER} of water weight in the first weeks. It doesn't ` +
          'replace a calorie surplus.',
      },
    ],
    quizBranch: 'protein',
  },
  {
    slug: 'lose-fat',
    label: 'Lose fat',
    description: 'I want to lose weight without losing muscle',
    icon: 'ph-fire',
    h1: 'Which supplements help you lose fat?',
    metaTitle: 'Which Supplements Help You Lose Fat? | WheyProof',
    metaDescription:
      "Few supplements help with fat loss: protein powder and caffeine. Why fat burners and L-carnitine aren't on the list, plus current protein powder prices.",
    answer:
      'Fat loss is driven by a calorie deficit, and no supplement creates one. Hitting your protein target helps with ' +
      'fullness and muscle retention, and protein powder is the lowest-calorie way to do it. Fat burners haven’t ' +
      'been shown to have a meaningful effect.',
    verdicts: [
      {
        level: 'priority',
        name: 'Protein powder (if needed)',
        reason:
          'In a calorie deficit, hitting your protein target helps limit muscle loss and keeps you full. If food falls ' +
          'short, powder is the lowest-calorie way to get there.',
        dose: PROTEIN_TARGET,
        link: PROTEIN_CALCULATOR,
        pickSectionId: 'protein-powder',
      },
      {
        level: 'optional',
        name: 'Caffeine',
        reason:
          'Can slightly reduce fatigue and appetite. The active ingredient in most fat burners is caffeine anyway; ' +
          'coffee does the same job.',
      },
      {
        level: 'optional',
        name: 'Creatine',
        reason: `Helps you keep your strength while dieting. It doesn't block fat loss; the ${CREATINE_WATER} it adds on the scale is water in muscle.`,
        dose: '3–5 g a day',
        link: CREATINE_DOSAGE,
      },
      {
        level: 'skip',
        name: 'Fat burners (thermogenics)',
        reason: "Haven't been shown to cause meaningful fat loss without a calorie deficit.",
        link: { label: 'Do fat burners work?', path: '/guides/do-fat-burners-work' },
      },
      {
        level: 'skip',
        name: 'L-carnitine and CLA',
        reason: 'Their effect on fat loss in healthy people has been either very small or inconsistent.',
      },
    ],
    picks: [PROTEIN_PICKS],
    faqs: [
      {
        question: 'Do fat burners work?',
        answer:
          "They haven't been shown to cause meaningful fat loss without a calorie deficit. In most of those with any " +
          'effect, the active ingredient is caffeine.',
      },
      {
        question: 'Should I use protein powder on a diet?',
        answer:
          "If you can't hit your protein target from food, yes: it's a low-calorie protein source and helps with " +
          "fullness. If you can, you don't need it.",
      },
      {
        question: 'Does L-carnitine burn fat?',
        answer: 'In healthy people its effect on fat loss has been either very small or inconsistent.',
      },
    ],
    quizBranch: 'protein',
  },
  {
    slug: 'endurance',
    label: 'Endurance',
    description: 'Running, cycling, swimming and other long efforts',
    icon: 'ph-person-simple-run',
    h1: 'Which supplements help with running and endurance?',
    metaTitle: 'Supplements for Running and Endurance | WheyProof',
    metaDescription:
      "Carbohydrate, electrolytes and caffeine for running, cycling and long sessions: when you need them and when you don't. Current drink mix prices.",
    answer:
      'In endurance sport, what helps performance most is carbohydrate during long efforts. Past 60–90 minutes, gels ' +
      'or a sports drink help; in the heat or with heavy sweating, electrolytes do. For shorter sessions, water is ' +
      'enough.',
    verdicts: [
      {
        level: 'priority',
        name: 'Carbohydrate: gels or a sports drink (long efforts)',
        reason:
          'The supplement that most supports performance in sessions and races over 60–90 minutes. Not needed for ' +
          'shorter ones.',
        dose: '30–60 g per hour; up to 90 g past 2.5 hours',
        pickSectionId: 'drink-mixes',
      },
      {
        level: 'priority',
        name: 'Electrolytes (especially sodium)',
        reason:
          'Replace the sodium lost in sweat in the heat and in long sessions; more important for heavy sweaters.',
        link: { label: 'Electrolytes explained', path: '/guides/electrolytes-explained' },
        pickSectionId: 'electrolytes',
      },
      {
        level: 'optional',
        name: 'Caffeine',
        reason: 'Well shown to improve endurance performance. Try it in training before a race.',
      },
      {
        level: 'optional',
        name: 'Beta-alanine',
        reason:
          'May give a small benefit in hard efforts lasting a few minutes; limited effect at a long, steady pace.',
        link: { label: 'Beta-alanine dosage', path: '/calculators/beta-alanine-dosage' },
      },
      {
        level: 'skip',
        name: 'BCAAs',
        reason: "Haven't been shown to improve endurance performance; the fuel in long efforts is carbohydrate.",
      },
    ],
    picks: [
      {
        id: 'drink-mixes',
        category: 'energy-gels-drinks',
        title: 'Energy drink mixes',
        note: "Gels are sold by count, so they aren't in the per-weight comparison; the category page lists them all.",
        allLink: { label: 'All energy gels & drinks', path: '/category/energy-gels-drinks' },
      },
      {
        id: 'electrolytes',
        category: 'hydration',
        title: 'Electrolyte mixes',
        note: 'Only mixes sold by weight are compared; stick packs sold by count are on the category page.',
        allLink: { label: 'All hydration products', path: '/category/hydration' },
      },
    ],
    faqs: [
      {
        question: 'When do I need gels for running?',
        answer:
          'On runs and races longer than 60–90 minutes. For shorter runs, the meal beforehand and water are enough.',
      },
      {
        question: 'Do I need an electrolyte drink?',
        answer: 'Sodium losses go up in long, sweaty sessions. For short, cool sessions, water is enough.',
      },
      {
        question: 'Should I try a new gel on race day?',
        answer: "No. Try gels and drinks in training first, so they don't upset your stomach.",
      },
    ],
    quizBranch: 'endurance',
  },
];

export function findSupplementGoal(slug: string): SupplementGoal | undefined {
  return SUPPLEMENT_GOALS.find((goal) => goal.slug === slug);
}

// ---- Quiz ------------------------------------------------------------------

export interface QuizOption {
  value: string;
  label: string;
  description?: string;
  icon?: string;
}

/** The questions after the goal; the answers go to the goal page as query parameters. */
export interface QuizQuestion {
  /** The query parameter name. */
  key: string;
  question: string;
  options: QuizOption[];
}

export const GOAL_QUESTION = 'What’s your main goal?';

export const QUIZ_FOLLOW_UPS: Record<QuizBranch, QuizQuestion[]> = {
  protein: [
    {
      key: 'protein',
      question: 'Can you hit your daily protein target from food?',
      options: [
        { value: 'yes', label: 'Yes, most days' },
        { value: 'no', label: 'No, I usually fall short' },
        { value: 'unsure', label: 'I don’t know', description: 'We’ll link a calculator in your result' },
      ],
    },
    {
      key: 'dairy',
      question: 'Any preference on dairy in a protein powder?',
      options: [
        { value: 'any', label: 'No preference' },
        { value: 'low-lactose', label: 'Low in lactose', description: 'Whey isolate and hydrolyzed whey' },
        { value: 'plant', label: 'Plant-based', description: 'Pea, rice or soy protein' },
      ],
    },
  ],
  endurance: [
    {
      key: 'duration',
      question: 'How long are your sessions usually?',
      options: [
        { value: 'short', label: 'Under an hour' },
        { value: 'medium', label: '1–2 hours' },
        { value: 'long', label: 'Over 2 hours' },
      ],
    },
    {
      key: 'sweat',
      question: 'Do you sweat a lot or train in the heat?',
      options: [
        { value: 'yes', label: 'Yes' },
        { value: 'no', label: 'No' },
      ],
    },
  ],
};

/** Only the answers in the URL that are valid for this goal's questions. */
export function validAnswers(goal: SupplementGoal, params: Record<string, string | undefined>): Record<string, string> {
  const answers: Record<string, string> = {};
  for (const question of QUIZ_FOLLOW_UPS[goal.quizBranch]) {
    const value = params[question.key];
    if (value && question.options.some((option) => option.value === value)) answers[question.key] = value;
  }
  return answers;
}

/** The quiz's dairy answer -> the protein list's narrowing type (/api/value-picks). */
export function proteinTypeFor(answers: Record<string, string>): 'isolate' | 'plant' | null {
  if (answers['dairy'] === 'low-lactose') return 'isolate';
  if (answers['dairy'] === 'plant') return 'plant';
  return null;
}

export interface PersonalNote {
  text: string;
  link?: GoalLink;
}

/**
 * The personal summary at the top of a goal page, built from the answers. The
 * general content (the verdict table) doesn't change; these notes only say
 * which part of it applies to this person.
 */
export function personalNotes(goal: SupplementGoal, answers: Record<string, string>): PersonalNote[] {
  const notes: PersonalNote[] = [];

  if (goal.quizBranch === 'protein') {
    const protein = answers['protein'];
    if (protein === 'yes') {
      notes.push({
        text: {
          'build-muscle': 'If you hit your protein target from food, protein powder won’t add muscle; spend your budget on creatine first.',
          'gain-weight': 'If you already hit your protein target, what’s missing is likely calories; a carb powder or a gainer closes that gap.',
          'lose-fat': 'If you hit your protein target from food, you barely need supplements; the calorie deficit and training do the work.',
        }[goal.slug] ?? '',
      });
    } else if (protein === 'no') {
      notes.push({
        text: {
          'build-muscle': 'If your protein falls short, protein powder is the easiest way to close the gap. With creatine, those two are enough.',
          'gain-weight': 'A gainer gives calories and protein together. The cheaper route: mix a carb powder with protein powder.',
          'lose-fat': 'On a diet, hitting your protein target matters for fullness and muscle retention; protein powder is the lowest-calorie way.',
        }[goal.slug] ?? '',
      });
    } else if (protein === 'unsure') {
      notes.push({
        text: 'Work out your daily protein need first; if food covers it, you may not need protein powder.',
        link: PROTEIN_CALCULATOR,
      });
    }

    const proteinType = proteinTypeFor(answers);
    if (proteinType === 'isolate') {
      notes.push({ text: 'We narrowed the protein powder list to isolate and hydrolyzed whey, which are very low in lactose.' });
    } else if (proteinType === 'plant') {
      notes.push({ text: 'We narrowed the protein powder list to plant-based products.' });
    }
  } else {
    const duration = answers['duration'];
    if (duration === 'short') {
      notes.push({
        text: 'For sessions under an hour you don’t need gels or a sports drink; water is enough. The products below are for long sessions and race days.',
      });
    } else if (duration === 'medium') {
      notes.push({ text: 'For 1–2 hour sessions, 30–60 g of carbohydrate per hour is the usual advice; gels or a drink make that easy.' });
    } else if (duration === 'long') {
      notes.push({
        text: 'Past 2 hours, up to 90 g of carbohydrate per hour is recommended; your stomach needs practice in training to handle it.',
      });
    }

    if (answers['sweat'] === 'yes') {
      notes.push({ text: 'If you sweat a lot or train in the heat, electrolytes, especially sodium, come first.' });
    } else if (answers['sweat'] === 'no') {
      notes.push({ text: 'In cool conditions and with light sweating, you usually don’t need electrolytes.' });
    }
  }

  return notes.filter((note) => note.text.length > 0);
}
