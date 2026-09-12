// Supplement glossary: aimed at long-tail "what is X" searches, a low-effort,
// high-return page. Definitions are short and honest; no firm medical
// claims, uncertainty stated plainly (same tone as the guides).

import { SITE_NAME } from './site-identity';

export interface GlossaryTerm {
  term: string;
  slug: string;
  definition: string;
  // Internal link to the related category page (when there is one), for
  // glossary <-> category cross-linking.
  relatedCategorySlug?: string;
}

export interface GlossaryGroup {
  heading: string;
  terms: GlossaryTerm[];
}

export const GLOSSARY: GlossaryGroup[] = [
  {
    heading: 'Protein Types',
    terms: [
      {
        term: 'Whey Concentrate (WPC)',
        slug: 'whey-concentrate',
        definition:
          'The most common and usually cheapest type of protein powder, made from whey. Typically 70-80% protein, ' +
          'with small amounts of fat and lactose.',
        relatedCategorySlug: 'protein-powder',
      },
      {
        term: 'Whey Isolate (WPI)',
        slug: 'whey-isolate',
        definition:
          'Whey concentrate put through an extra filtration step. Protein rises to 85-95% and lactose drops to ' +
          'almost zero, so it is a common choice for people with lactose sensitivity.',
        relatedCategorySlug: 'protein-powder',
      },
      {
        term: 'Hydrolyzed Whey (WPH)',
        slug: 'hydrolyzed-whey',
        definition:
          'Whey whose protein is partly broken down in advance, in theory absorbed faster. Usually more expensive; ' +
          'for most users the practical difference from isolate or concentrate is small.',
        relatedCategorySlug: 'protein-powder',
      },
      {
        term: 'Casein',
        slug: 'casein',
        definition:
          'The second main milk protein, digested much more slowly than whey. Usually taken before bed or for long ' +
          'gaps between meals rather than for quick post-workout recovery.',
        relatedCategorySlug: 'protein-powder',
      },
      {
        term: 'Plant Protein',
        slug: 'plant-protein',
        definition:
          'Protein powder from sources such as pea, rice, hemp or soy, with no dairy. A single plant source often ' +
          'has an incomplete amino acid profile, so good products blend several.',
        relatedCategorySlug: 'protein-powder',
      },
    ],
  },
  {
    heading: 'Amino Acids',
    terms: [
      {
        term: 'BCAA',
        slug: 'bcaa',
        definition:
          'The three branched-chain amino acids (leucine, isoleucine, valine), focused on the leucine signal that ' +
          'triggers muscle protein synthesis. A subset of the EAAs.',
        relatedCategorySlug: 'amino-acids',
      },
      {
        term: 'EAA',
        slug: 'eaa',
        definition:
          'All nine essential amino acids the body can\'t make itself (the three BCAAs are among them). Building ' +
          'muscle protein needs the other eight, not just the leucine signal.',
        relatedCategorySlug: 'amino-acids',
      },
      {
        term: 'Glutamine',
        slug: 'glutamine',
        definition:
          'An amino acid linked to immune and gut health. Its direct effect on muscle growth is not as well ' +
          'supported as BCAAs or EAAs.',
        relatedCategorySlug: 'amino-acids',
      },
      {
        term: 'Citrulline',
        slug: 'citrulline',
        definition:
          'An amino acid that supports blood flow and adds to the "pump" during training. Effective doses are ' +
          'usually around 6-8 g of citrulline malate; many budget products use less.',
        relatedCategorySlug: 'pre-workout',
      },
      {
        term: 'Beta-Alanine',
        slug: 'beta-alanine',
        definition:
          'An amino acid that raises carnosine in muscle and can help delay fatigue in short, high-intensity ' +
          'efforts. Tingling of the skin (paresthesia) is a harmless, well-known side effect.',
        relatedCategorySlug: 'pre-workout',
      },
      {
        term: 'Taurine',
        slug: 'taurine',
        definition:
          'An amino acid found in energy drinks and some pre-workouts. Its effect on performance is less well ' +
          'supported than ingredients such as caffeine or beta-alanine.',
        relatedCategorySlug: 'pre-workout',
      },
      {
        term: 'ALCAR (Acetyl-L-Carnitine)',
        slug: 'alcar',
        definition:
          'A form of L-carnitine thought to cross the blood-brain barrier more easily than standard L-carnitine. ' +
          'Also mentioned for cognitive effects, where evidence is limited too.',
        relatedCategorySlug: 'fat-burners',
      },
    ],
  },
  {
    heading: 'Performance Supplements',
    terms: [
      {
        term: 'Creatine Monohydrate',
        slug: 'creatine-monohydrate',
        definition:
          'The oldest, most researched and usually cheapest form of creatine. Its effect on strength and power ' +
          'is among the best supported of any supplement.',
        relatedCategorySlug: 'creatine',
      },
      {
        term: 'Creatine HCl',
        slug: 'creatine-hcl',
        definition:
          'Creatine hydrochloride, marketed as causing less water retention or stomach upset. Far fewer large ' +
          'studies support those claims than support monohydrate.',
        relatedCategorySlug: 'creatine',
      },
      {
        term: 'Loading Phase',
        slug: 'loading-phase',
        definition:
          'Taking a high dose of creatine (about 20 g a day) for the first 5-7 days to fill muscle stores quickly. ' +
          'Skipping it and starting at a low dose reaches the same saturation within 3-4 weeks; loading is only ' +
          'about speed.',
        relatedCategorySlug: 'creatine',
      },
      {
        term: 'Thermogenic',
        slug: 'thermogenic',
        definition:
          'Ingredients thought to raise metabolic rate slightly and briefly (caffeine, green tea extract). The ' +
          'effect is tiny next to a daily calorie deficit and doesn\'t cause fat loss on its own.',
        relatedCategorySlug: 'fat-burners',
      },
      {
        term: 'Electrolytes',
        slug: 'electrolytes',
        definition:
          'Minerals such as sodium, potassium and magnesium that help regulate fluid balance and muscle function. ' +
          'Sodium is the one lost in the largest amounts in sweat.',
        relatedCategorySlug: 'hydration',
      },
      {
        term: 'Bioavailability',
        slug: 'bioavailability',
        definition:
          'How much of a nutrient the body can actually use. The total amount on a label (e.g. "24 g protein") ' +
          'doesn\'t tell the whole story.',
      },
    ],
  },
  {
    heading: 'Weight Gain',
    terms: [
      {
        term: 'Mass Gainer',
        slug: 'mass-gainer',
        definition:
          'A high-calorie supplement with far more carbohydrate than a standard protein powder, designed to make ' +
          'gaining weight easier. Usually unnecessary for people who gain weight easily.',
        relatedCategorySlug: 'mass-gainers',
      },
      {
        term: 'Maltodextrin',
        slug: 'maltodextrin',
        definition:
          'A fast-digesting carbohydrate common in gainers. It can spike blood sugar; some better products use ' +
          'slower sources such as oats instead.',
        relatedCategorySlug: 'mass-gainers',
      },
    ],
  },
  {
    heading: 'Vitamins and Minerals',
    terms: [
      {
        term: 'Multivitamin',
        slug: 'multivitamin',
        definition:
          'A supplement with many vitamins and minerals at low to moderate doses to help prevent general gaps. ' +
          'It doesn\'t target a specific deficiency; for one found in a blood test, a single, higher-dose ' +
          'supplement is usually more effective.',
        relatedCategorySlug: 'vitamins',
      },
      {
        term: 'ZMA',
        slug: 'zma',
        definition:
          'A combination of zinc, magnesium and vitamin B6, claimed to support sleep and recovery. Evidence for ' +
          'those claims is limited.',
        relatedCategorySlug: 'vitamins',
      },
      {
        term: 'Daily Value (%DV)',
        slug: 'daily-value',
        definition:
          'The percentage of a reference daily intake that one serving provides, shown on the Supplement Facts ' +
          'panel. Useful for comparing how much of each ingredient a product really contains.',
        relatedCategorySlug: 'vitamins',
      },
    ],
  },
  {
    heading: 'Fat Burners',
    terms: [
      {
        term: 'CLA (Conjugated Linoleic Acid)',
        slug: 'cla',
        definition:
          'A fatty acid found naturally in meat and dairy, said to affect fat storage. Results in human studies ' +
          'are inconsistent.',
        relatedCategorySlug: 'fat-burners',
      },
      {
        term: 'L-Carnitine',
        slug: 'l-carnitine',
        definition:
          'A compound that carries fatty acids to the part of the cell that produces energy. The body already ' +
          'makes enough, and the effect of extra supplemental carnitine is more modest than expected.',
        relatedCategorySlug: 'fat-burners',
      },
    ],
  },
  {
    heading: 'Prices and Labels',
    terms: [
      {
        term: 'Price per Serving',
        slug: 'price-per-serving',
        definition:
          'The package price divided by the number of servings in it, the real cost. A big tub can look "cheap" ' +
          'and still cost more per serving.',
      },
      {
        term: 'Amino Spiking',
        slug: 'amino-spiking',
        definition:
          'The practice of adding cheap free amino acids (such as glycine or taurine) to inflate the protein ' +
          'number on the label. Third-party testing helps catch it.',
      },
      {
        term: 'Proprietary Blend',
        slug: 'proprietary-blend',
        definition:
          'A label listing that gives the total weight of a group of ingredients but not the dose of each. It ' +
          'makes it impossible to tell whether an ingredient is present at an effective amount.',
      },
      {
        term: 'Third-Party Testing',
        slug: 'third-party-testing',
        definition:
          'Testing by a lab independent of the manufacturer that a product contains what the label says and no ' +
          'banned substances. NSF Certified for Sport and Informed Sport are well-known programs.',
      },
      {
        term: `Real Price Drop (${SITE_NAME} definition)`,
        slug: 'real-price-drop',
        definition:
          'A product whose current price is genuinely below its highest price in the last 30 days. It rests on ' +
          `the price history we collect, not on the store's own "was/now" claim; ${SITE_NAME} shows the two ` +
          'separately as "Real price drops" and "Store sales".',
      },
    ],
  },
];
