using System.Net.Mail;

namespace IndirimTakip.Api.Endpoints;

// Helpers shared by the endpoint definitions. They were top-level local
// functions in Program.cs; once endpoints moved into separate files they
// became unreachable there, so they live in one shared class. Behavior unchanged.
internal static class EndpointHelpers
{
    // A successful confirmation gets its own, stronger success surface, in the
    // same visual language as the email, so the visitor can go straight to the
    // current deals. Neutral states (invalid link, unsubscribed) keep using the
    // compact info page below.
    internal static string BuildSubscriptionConfirmedPage(string frontendBaseUrl)
    {
        var baseUrl = frontendBaseUrl.TrimEnd('/');
        var signalImageUrl = $"{baseUrl}/email-assets/subscription-confirmed-signal.jpg";
        var logoUrl = $"{baseUrl}/icons/icon-192x192.png";
        var mailIconUrl = $"{baseUrl}/email-assets/step-confirm.png";
        var shieldImageUrl = $"{baseUrl}/email-assets/trust-shield.png";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="color-scheme" content="light only">
              <title>You're subscribed! | WheyProof</title>
              <style>
                * { box-sizing:border-box; }
                html, body { min-height:100%; }
                body {
                  margin:0;
                  background:#f7f7fc;
                  color:#171a2e;
                  font-family:Arial,Helvetica,sans-serif;
                  -webkit-font-smoothing:antialiased;
                }
                .confirmation-page {
                  min-height:100vh;
                  display:flex;
                  flex-direction:column;
                  align-items:center;
                  justify-content:flex-start;
                  padding:48px 24px 56px;
                }
                .brand-link {
                  display:inline-flex;
                  align-items:center;
                  gap:14px;
                  margin-bottom:36px;
                  color:#171a2e;
                  text-decoration:none;
                }
                .brand-link img { width:58px; height:58px; display:block; }
                .brand-name {
                  font-size:32px;
                  font-weight:800;
                  line-height:1;
                  letter-spacing:-1.2px;
                  white-space:nowrap;
                }
                .brand-name span { color:#6556e8; }
                .confirmation-card {
                  width:min(100%, 1120px);
                  overflow:hidden;
                  background:#ffffff;
                  border:1px solid #e4e6ef;
                  border-radius:20px;
                  box-shadow:0 18px 54px rgba(20,24,48,.14);
                }
                .confirmation-hero {
                  min-height:675px;
                  padding:80px 86px;
                  background-color:#0e1122;
                  background-image:url('{{signalImageUrl}}');
                  background-position:center;
                  background-repeat:no-repeat;
                  background-size:cover;
                }
                .confirmation-content { width:44%; }
                .eyebrow {
                  display:inline-flex;
                  align-items:center;
                  gap:10px;
                  padding:12px 20px 12px 14px;
                  border:1px solid #796cbf;
                  border-radius:999px;
                  background:#2b2741;
                  color:#f5f6fb;
                  font-size:17px;
                  font-weight:800;
                  line-height:22px;
                  letter-spacing:.9px;
                }
                .eyebrow img { width:28px; height:28px; display:block; }
                h1 {
                  margin:32px 0 22px;
                  color:#ffffff;
                  font-size:72px;
                  font-weight:800;
                  line-height:1.04;
                  letter-spacing:-2.1px;
                }
                .confirmation-copy {
                  margin:0;
                  color:#c7cbe0;
                  font-size:25px;
                  line-height:1.5;
                }
                .primary-action {
                  display:block;
                  width:100%;
                  margin-top:36px;
                  padding:26px;
                  border-radius:10px;
                  background:#6556e8;
                  color:#ffffff;
                  font-size:22px;
                  font-weight:800;
                  line-height:24px;
                  text-align:center;
                  text-decoration:none;
                  box-shadow:0 10px 24px rgba(101,86,232,.24);
                  transition:background-color .18s ease, transform .18s ease;
                }
                .primary-action:hover { background:#7567ec; transform:translateY(-1px); }
                .primary-action:focus-visible { outline:3px solid #b5abfc; outline-offset:4px; }
                .trust-strip {
                  display:flex;
                  align-items:center;
                  gap:18px;
                  min-height:145px;
                  padding:38px 86px;
                  background:#ffffff;
                  color:#303548;
                  font-size:19px;
                  line-height:1.5;
                }
                .trust-strip img { width:76px; height:76px; display:block; flex:0 0 auto; }
                @media (max-width:760px) {
                  .confirmation-page { justify-content:flex-start; padding:28px 14px; }
                  .brand-link { margin-bottom:24px; gap:10px; }
                  .brand-link img { width:44px; height:44px; }
                  .brand-name { font-size:23px; letter-spacing:-.8px; }
                  .confirmation-card { border-radius:16px; }
                  .confirmation-hero {
                    min-height:630px;
                    padding:42px 26px 270px;
                    background-position:67% center;
                  }
                  .confirmation-content { width:100%; }
                  .eyebrow { padding:8px 13px 8px 9px; gap:8px; font-size:12px; line-height:18px; }
                  .eyebrow img { width:22px; height:22px; }
                  h1 { margin-top:22px; font-size:40px; line-height:1.06; letter-spacing:-1.3px; }
                  .confirmation-copy { font-size:17px; line-height:1.5; }
                  .primary-action { margin-top:26px; padding:18px 26px; font-size:17px; }
                  .trust-strip { align-items:flex-start; padding:24px 24px; gap:12px; font-size:14px; }
                  .trust-strip img { width:42px; height:42px; }
                }
                @media (max-width:380px) {
                  .brand-name { font-size:21px; }
                  .confirmation-hero { padding-left:22px; padding-right:22px; }
                  h1 { font-size:36px; }
                }
              </style>
            </head>
            <body>
              <main class="confirmation-page">
                <a class="brand-link" href="{{baseUrl}}" aria-label="WheyProof home page">
                  <img src="{{logoUrl}}" width="54" height="54" alt="">
                  <span class="brand-name">WHEY<span>PROOF</span></span>
                </a>
                <section class="confirmation-card" aria-labelledby="confirmation-heading">
                  <div class="confirmation-hero">
                    <div class="confirmation-content">
                      <div class="eyebrow"><img src="{{mailIconUrl}}" width="24" height="24" alt="">SUBSCRIPTION ACTIVE</div>
                      <h1 id="confirmation-heading">You're<br>subscribed!</h1>
                      <p class="confirmation-copy">Real price drops and the week's top deals will now arrive in your inbox.</p>
                      <a class="primary-action" href="{{baseUrl}}">See the deals</a>
                    </div>
                  </div>
                  <div class="trust-strip">
                    <img src="{{shieldImageUrl}}" width="52" height="52" alt="">
                    <span>You can change your email preferences at any time.</span>
                  </div>
                </section>
              </main>
            </body>
            </html>
            """;
    }

    // frontendBaseUrl comes from configuration: a hard-coded address left behind
    // after a domain change is a real risk.
    internal static string BuildInfoPage(string heading, string message, string frontendBaseUrl) => $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>WheyProof</title>
        </head>
        <body style="margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;box-sizing:border-box;background:#fafaf9;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <div style="max-width:420px;width:100%;background:#ffffff;border-radius:16px;box-shadow:0 4px 24px rgba(0,0,0,0.08);padding:40px 32px;text-align:center;">
            <div style="display:inline-flex;align-items:center;gap:8px;margin-bottom:28px;">
              <div style="width:36px;height:36px;border-radius:8px;background:#6556e8;color:#fff;font-weight:800;font-size:14px;display:flex;align-items:center;justify-content:center;">WP</div>
              <span style="font-size:18px;font-weight:700;color:#1c1917;">Whey<span style="color:#6556e8;">Proof</span></span>
            </div>
            <h1 style="font-size:20px;font-weight:800;color:#1c1917;margin:0 0 8px;">{heading}</h1>
            <p style="font-size:14px;color:#78716c;margin:0 0 28px;line-height:1.5;">{message}</p>
            <a href="{frontendBaseUrl}" style="display:inline-block;background:#6556e8;color:#fff;text-decoration:none;font-weight:600;font-size:14px;padding:12px 28px;border-radius:9999px;">Back to the site</a>
          </div>
        </body>
        </html>
        """;

    // Same card as BuildInfoPage, but the button submits a POST back to the link.
    // Email link scanners (Gmail's included) open every link they find; a GET
    // that changed state let them confirm subscriptions nobody asked for. They
    // don't submit forms, so the change only happens on a real click.
    // actionPath carries the token from the URL, which is visitor input: it is
    // escaped as a path segment by the caller and HTML-encoded here.
    internal static string BuildActionPage(string heading, string message, string buttonLabel, string actionPath, string frontendBaseUrl)
    {
        var action = System.Net.WebUtility.HtmlEncode(actionPath);
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex">
              <title>WheyProof</title>
            </head>
            <body style="margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;box-sizing:border-box;background:#fafaf9;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
              <div style="max-width:420px;width:100%;background:#ffffff;border-radius:16px;box-shadow:0 4px 24px rgba(0,0,0,0.08);padding:40px 32px;text-align:center;">
                <div style="display:inline-flex;align-items:center;gap:8px;margin-bottom:28px;">
                  <div style="width:36px;height:36px;border-radius:8px;background:#6556e8;color:#fff;font-weight:800;font-size:14px;display:flex;align-items:center;justify-content:center;">WP</div>
                  <span style="font-size:18px;font-weight:700;color:#1c1917;">Whey<span style="color:#6556e8;">Proof</span></span>
                </div>
                <h1 style="font-size:20px;font-weight:800;color:#1c1917;margin:0 0 8px;">{heading}</h1>
                <p style="font-size:14px;color:#78716c;margin:0 0 28px;line-height:1.5;">{message}</p>
                <form method="post" action="{action}" style="margin:0 0 16px;">
                  <button type="submit" style="display:inline-block;border:0;cursor:pointer;background:#6556e8;color:#fff;font-weight:700;font-size:15px;padding:13px 32px;border-radius:9999px;">{buttonLabel}</button>
                </form>
                <a href="{frontendBaseUrl}" style="color:#78716c;font-size:13px;">Back to the site</a>
              </div>
            </body>
            </html>
            """;
    }

    // Checking only for "@" let strings that are invalid even by RFC 5322
    // ("a;b@x.com", "a\"b@x.com") into the Subscribers table. SQL injection was
    // never possible (EF Core parameterizes queries); this is data hygiene, and
    // MailAddress's own format check means no regex to maintain.
    internal static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            return new System.Net.Mail.MailAddress(email).Address == email.Trim();
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // pageSize has an upper bound: an unbounded ?pageSize=5000000 would mean a
    // huge sorted query.
    internal static int NormalizePageSize(int? pageSize) => pageSize is null or <= 0 ? 24 : Math.Min(pageSize.Value, 100);
}
