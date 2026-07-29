# Critical Sequences

## Login And Session Validation

```mermaid
sequenceDiagram
    actor Client
    participant API as Auth/User API
    participant Auth as AuthLoginUseCase
    participant DB as SQL Server
    participant JWT as AuthTokenIssuer

    Client->>API: POST /api/auth/login
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
    actor Customer
    participant API as OrderController
    participant Dispatch as ShipmentDispatchUseCase
    participant Delivery as ShipmentDeliveryUseCase
    participant ReturnRequest as OrderReturnRequestUseCase
    participant ReturnReview as OrderReturnReviewUseCase
    participant ReturnReceipt as OrderReturnReceiptUseCase
    participant Refund as OrderRefundUseCase
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

    Staff->>API: POST /api/orders/{id}/refund + receipt reference
    API->>Refund: RecordRefundAsync
    Refund->>DB: Lock order/payment/return request
    Refund->>Rules: Require received return and Paid payment
    Refund->>DB: Set Order/Return/Payment Refunded + histories + commit
    API-->>Staff: Updated OrderResponse
```

Receiving returned goods and offline COD refund are separate auditable actions. Replaying the same refund
reference is idempotent; a different reference cannot overwrite the recorded financial history.
