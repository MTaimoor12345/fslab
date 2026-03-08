# SportsStore Modernization Project

## 👤 Student Details
- **Full Name:** Shah Fahad
- **Student ID:** 73876

---

## 🚀 Overview
This project is a modernize version of the SportsStore application, upgraded to **.NET 10.0**. It includes structured logging with **Serilog**, secure payment processing with **Stripe**, and an automated CI pipeline using **GitHub Actions**.

## 🛠 Features & Updates

### 🟢 Part A: .NET 10 Upgrade
- Upgraded project targeting from `net6.0` to `net10.0`.
- All NuGet dependencies updated to version `10.0.0` or latest compatible versions (EF Core, Identity, etc.).
- Verified solution stability and resolved cross-version dependencies.
Part A : Implemented 

### 🟢 Part B: Serilog Integration
- Integrated **Serilog.AspNetCore** for structured logging.
- Configured logging via `appsettings.json`.
- **Sinks:**
    - **Console:** Colored logs for development.
    - **File:** Daily rolling JSON formatted logs in `Logs/` directory.
- **Structured Properties:** Logged startup events, checkout attempts, order creation success, and unhandled exceptions.

Part B : Implemented 

### 🟢 Part C: Stripe Payment Integration
- Integrated official **Stripe.net SDK**.
- **Service Layer:** `IStripePaymentService` / `StripePaymentService` for payment logic.
- **Checkout Flow:** Payment is processed and confirmed via Stripe *before* the order is saved to the database.
- **Success / Cancel / Failure:** Handled with logging (OrderController + Seq).
- **Card declined:** Stripe sends `payment_intent.payment_failed` to our **webhook**; we log it so it appears in **Seq** (see Webhook setup below).
- **Data Security:** API keys and webhook secret via User Secrets.

### 🟡 Part D: GitHub Actions CI
- Created workflow in `.github/workflows/ci.yml`.
- Automatically restores dependencies, builds the solution, and runs tests.
- Triggers on push to `main` and all pull requests.
- Includes automated test reporting.

---

## 🏗 How to Run Locally

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server LocalDB
- https://datalust.co/seq

### 2. Stripe Configuration (Security)
DO NOT hardcode keys in `appsettings.json`. Instead, use **User Secrets**:
```bash


dotnet user-secrets init
dotnet user-secrets set "Stripe:PublishableKey" "your_pk_test_..."
dotnet user-secrets set "Stripe:SecretKey" "your_sk_test_..."
```

### Card declined → log in Seq (webhook)
When a card is **declined** on Stripe’s page, Stripe notifies our app via a **webhook**. We log that event so it appears in **Seq**.

1. **Local testing:** Use [Stripe CLI](https://stripe.com/docs/stripe-cli):
   ```bash
   stripe listen --forward-to https://localhost:7XXX/webhooks/stripe
   ```
   (Replace port with your app’s HTTPS port.) The CLI will print a **webhook signing secret** (`whsec_...`).

2. **Set the secret** (from project folder `SportsStore`):
   ```bash
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."
   ```

3. **Decline test:** Use card `4000 0000 0000 0002` at checkout. After decline, Stripe sends `payment_intent.payment_failed` → our app logs it → **Seq** shows:  
   `Stripe card declined / payment failed. PaymentIntentId: ..., ErrorCode: ..., ErrorMessage: ...`

### 3. Build & Run
```bash
dotnet restore
dotnet build
dotnet run --project SportsStore
```

---

Seq sink: http://localhost:5341 default Seq URL

Working  Card  4242 4242 4242 4242
not working card 4000 0000 0000 0002
