# Critical Sequences

## Login And Session Validation

```mermaid
sequenceDiagram
    actor Client
    participant API as Auth/User API
    participant Auth as AuthSessionService
    participant DB as SQL Server
    participant JWT as AuthTokenIssuer

    Client->>API: POST /api/auth/login
    API->>Auth: LoginAsync(credentials)
    Auth->>DB: Lock user and load roles/permissions
    Auth->>Auth: Constant-work BCrypt verification
    Auth->>DB: Insert hashed refresh token family
    Auth->>JWT: Create access token with session/version claims
    Auth->>DB: Commit
    API-->>Client: Access token + refresh token

    Client->>API: GET protected endpoint + Bearer token
    API->>JWT: Validate signature, issuer, audience, expiry
    API->>DB: Check user token version and active token family
    alt Session active
        API-->>Client: Protected response
    else Revoked, expired or reused
        API-->>Client: 401 ProblemDetails
    end
```

## Idempotent COD Checkout

```mermaid
sequenceDiagram
    actor Customer
    participant API as OrderController
    participant Checkout as OrderCheckoutUseCase
    participant Pricing as OrderPricingUseCase
    participant Rules as Domain Policies
    participant DB as SQL Server
    participant Outbox as Outbox Dispatcher

    Customer->>API: POST /api/orders + Idempotency-Key
    API->>Checkout: PlaceOrderAsync
    Checkout->>DB: Begin transaction
    Checkout->>DB: Check key, lock cart, recheck key
    Checkout->>DB: Lock products in stable ID order
    Checkout->>DB: Lock promotion and count customer redemptions
    Checkout->>Pricing: Recalculate discount, shipping, tax and total
    Pricing->>Rules: Validate promotion, price, stock and order totals
    Checkout->>Rules: Reserve inventory
    Checkout->>DB: Insert order snapshots, promotion redemption, payment, histories, ledger
    Checkout->>DB: Clear cart and append outbox message
    Checkout->>DB: Commit
    Checkout-->>API: OrderResponse
    API-->>Customer: 201 Created
    Outbox->>DB: Claim committed message
    Outbox-->>Customer: Notification (at-least-once)
```

Concurrent retries with the same user and key return the original order. Reusing the key with a
different request returns `409`; unavailable stock rolls back the complete transaction.

## Delivery, Return And Refund

```mermaid
sequenceDiagram
    actor Staff
    participant API as OrderController
    participant Commands as OrderCommandService
    participant Refund as OrderRefundUseCase
    participant Rules as Order/Payment/Inventory Policies
    participant DB as SQL Server

    Staff->>API: PUT /api/orders/{id}/status
    API->>Commands: UpdateStatusAsync
    Commands->>DB: Begin transaction and lock order/payment
    Commands->>Rules: Validate next order/payment state
    Commands->>DB: Append status/payment history and commit

    Staff->>API: Mark Returned after delivered order is received
    API->>Commands: UpdateStatusAsync(Returned)
    Commands->>DB: Lock products in stable order
    Commands->>Rules: Release returned quantity
    Commands->>DB: Append unique return inventory movements and commit

    Staff->>API: POST /api/orders/{id}/refund + receipt reference
    API->>Refund: RecordRefundAsync
    Refund->>DB: Lock order/payment
    Refund->>Rules: Require Returned order and Paid payment
    Refund->>DB: Append Refunded history and commit
    API-->>Staff: Updated OrderResponse
```

Return acceptance and offline COD refund are separate auditable actions. Replaying the same refund
reference is idempotent; a different reference cannot overwrite the recorded financial history.
