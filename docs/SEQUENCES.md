# Critical Sequences

## Login And Session Validation

```mermaid
sequenceDiagram
    actor Client
    participant API as Auth/User API
    participant Auth as AuthLoginUseCase
    participant DB as SQL Server
    participant JWT as AuthTokenIssuer

    Client->>API: POST /api/v1/auth/login
    API->>Auth: ExecuteAsync(credentials)
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

## Idempotent Checkout And Online Payment

```mermaid
sequenceDiagram
    actor Customer
    participant API as OrderController
    participant Checkout as OrderCheckoutUseCase
    participant Pricing as OrderPricingUseCase
    participant Rules as Domain Policies
    participant DB as SQL Server
    participant Stripe as Stripe API
    participant Webhook as Payment Webhook
    participant Outbox as Outbox Dispatcher

    Customer->>API: POST /api/v1/orders + Idempotency-Key
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
    alt Card payment
        Customer->>API: POST /payments/orders/{orderId}/initialize
        API->>DB: Claim external-creation lease + commit
        API->>Stripe: Create PaymentIntent outside SQL transaction
        Stripe-->>API: Provider ID, status and client secret
        API->>DB: Attach provider ID/status + commit
        Stripe->>Webhook: Signed payment event
        Webhook->>Webhook: Verify signature, amount, currency and event ID
        Webhook->>DB: Lock order/payment, apply state + audit/outbox + commit
    end
    Outbox->>DB: Claim committed message
    Outbox-->>Customer: Notification (at-least-once)
```

Concurrent retries with the same user and key return the original order. Reusing the key with a
different request returns `409`; unavailable stock rolls back the complete transaction. Stripe
network I/O never runs while checkout or inventory locks are held. A missing webhook is repaired
by the reconciliation worker querying a bounded batch of stale active PaymentIntents and locking
each order/payment before applying the observed state.

## Delivery, Return And Refund

```mermaid
sequenceDiagram
    actor Staff
    actor Customer
    participant API as OrderController
    participant Dispatch as ShipmentDispatchUseCase
    participant Delivery as ShipmentDeliveryUseCase
    participant ReturnRequest as OrderReturnRequestUseCase
    participant ReturnReview as OrderReturnReviewUseCase
    participant ReturnReceipt as OrderReturnReceiptUseCase
    participant Refund as Offline/Online Refund Use Case
    participant Gateway as Stripe API
    participant Rules as Order/Payment/Inventory Policies
    participant DB as SQL Server

    Staff->>API: POST shipment/dispatch (carrier + tracking)
    API->>Dispatch: ExecuteAsync
    Dispatch->>DB: Lock order and shipment
    Dispatch->>DB: Insert shipment + Shipping history + commit

    Staff->>API: POST shipment/deliver
    API->>Delivery: ExecuteAsync
    Delivery->>DB: Lock order, shipment and payment
    Delivery->>Rules: Delivered + collect COD
    Delivery->>DB: Commit histories atomically

    Customer->>API: POST return-request
    API->>ReturnRequest: ExecuteAsync
    ReturnRequest->>DB: Verify owner, delivery time and return window
    ReturnRequest->>DB: Insert request + ReturnRequested history
    Staff->>API: POST return-request/review
    API->>ReturnReview: ExecuteAsync
    ReturnReview->>DB: Approve or reject under order lock
    Staff->>API: POST return-request/receive
    API->>ReturnReceipt: ExecuteAsync
    ReturnReceipt->>DB: Lock products in stable order
    ReturnReceipt->>Rules: Receive inspection and release stock once
    ReturnReceipt->>DB: Append Returned history + ledger + commit

    Staff->>API: POST /api/v1/orders/{id}/refund + idempotency reference
    API->>Refund: ExecuteAsync
    Refund->>DB: Lock order/payment/return request
    Refund->>Rules: Require received return and Paid payment
    alt COD
        Refund->>DB: Record manual refund + histories + commit
    else Card
        Refund->>DB: Reserve PaymentRefund + commit
        Refund->>Gateway: Create refund outside SQL transaction
        Gateway-->>Refund: Provider refund ID/status
        Refund->>DB: Apply partial/full refund + histories/audit/outbox + commit
    end
    API-->>Staff: Updated OrderResponse
```

Receiving returned goods and refund are separate auditable actions. Replaying the same reference is
idempotent; a reused reference with different content cannot overwrite financial history. Online
refunds preserve payment currency and order base-currency snapshots, and the cumulative amount is
bounded by the captured payment.

## Email Verification And Password Reset

```mermaid
sequenceDiagram
    actor User
    participant API as AuthController
    participant Auth as Auth Use Case
    participant DB as SQL Server
    participant Outbox as Outbox Dispatcher
    participant SMTP as SMTP Provider

    User->>API: Request verification/reset
    API->>Auth: Normalize request without account disclosure
    Auth->>DB: Store token hash + protected outbox payload atomically
    Outbox->>DB: Claim committed message
    Outbox->>SMTP: Send with deterministic Message-ID
    User->>API: Submit raw one-time token
    API->>Auth: Hash token, lock user/token, validate expiry and use
    alt Password reset
        Auth->>DB: Change BCrypt hash, increment token version, revoke sessions
    else Email verification
        Auth->>DB: Set EmailVerifiedAt and consume token
    end
    Auth->>DB: Commit
```

Raw tokens are not stored as readable database values. Email verification is recorded but is not
currently a prerequisite for login.
