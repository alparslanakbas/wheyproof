import { displayName } from './display-name';

describe('displayName', () => {
  it('turns ALL CAPS product names into Title Case', () => {
    expect(displayName('WHEY ISOLATE')).toBe('Whey Isolate');
    expect(displayName('GOLD STANDARD 100% WHEY')).toBe('Gold Standard 100% Whey');
  });

  it('keeps known acronyms in capitals', () => {
    expect(displayName('NUTRICOST BCAA 2:1:1 POWDER')).toBe('Nutricost BCAA 2:1:1 Powder');
    expect(displayName('GNC AMP WHEYBOLIC')).toBe('GNC AMP Wheybolic');
  });

  it('keeps US weight units in capitals when the store wrote them so', () => {
    expect(displayName('GOLD STANDARD 100% WHEY 5 LB')).toBe('Gold Standard 100% Whey 5 LB');
  });

  it('leaves unit spellings that are already lower case alone', () => {
    expect(displayName('Nutricost Creatine 500g')).toBe('Nutricost Creatine 500g');
  });

  it("doesn't break parentheses and numbers", () => {
    expect(displayName('NOX2 540 G (36 G*15 SERVINGS)')).toBe('NOX2 540 G (36 G*15 Servings)');
  });

  it('keeps joining words lower case', () => {
    expect(displayName('CREAM OF RICE')).toBe('Cream of Rice');
    expect(displayName('PEANUT BUTTER AND JELLY')).toBe('Peanut Butter and Jelly');
  });

  it('keeps short tokens that look like joining words but are not', () => {
    // "a", "in" and "on" are deliberately not in the list, so "VITAMIN A"
    // doesn't become "Vitamin a".
    expect(displayName('VITAMIN A')).toBe('Vitamin A');
    expect(displayName('VITAMIN D3 K2')).toBe('Vitamin D3 K2');
  });

  it('changes nothing in names already in Title Case (idempotent)', () => {
    const already = 'Nutricost Creatine 500g';
    expect(displayName(already)).toBe(already);
  });

  it("doesn't fail on an empty string", () => {
    expect(displayName('')).toBe('');
  });

  // Upper/lower casing is locale-independent: a Turkish locale turned the
  // I in English words into a dotless ı ("Proteın") on the earlier site.
  it('lower-cases the letter I the English way', () => {
    expect(displayName('PROTEIN BAR')).toBe('Protein Bar');
    expect(displayName('HYALURONIC ACID')).toBe('Hyaluronic Acid');
    expect(displayName('MAGNESIUM BISGLYCINATE')).toBe('Magnesium Bisglycinate');
    expect(displayName('DAILY MULTIVITAMIN')).toBe('Daily Multivitamin');
  });

  it('handles accented letters in flavor names', () => {
    expect(displayName('AÇAÍ BERRY')).toBe('Açaí Berry');
  });

  it('keeps acronyms from real catalog names in capitals', () => {
    expect(displayName('GNC CoQ-10 100 mg 30 Softgels')).toContain('GNC');
    expect(displayName('GNC 5 - HTP')).toBe('GNC 5 - HTP');
    expect(displayName('GNC AMP - Wheybolic – 1300 g')).toContain('AMP');
    expect(displayName('GNC FOLATE 400 MCG 100 TABLETS')).toContain('MCG');
  });

  // The unit spelling comes FROM THE SOURCE; having MCG on the list must not
  // upper-case a lower-case "mcg" (same as g/ml/kg).
  it("doesn't upper-case a unit written in lower case", () => {
    expect(displayName('Biotin 5000 mcg 120 Capsules')).toContain('mcg');
  });

  // An ingredient already written in mixed case must be left alone.
  it("doesn't break a mixed-case ingredient name", () => {
    expect(displayName('GNC CoQ-10 100 mg')).toContain('CoQ-10');
  });
});
