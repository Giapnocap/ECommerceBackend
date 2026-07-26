# Database ERD

The database keeps current operational state and immutable histories in the same SQL Server
database. Order details preserve product name and price snapshots; inventory and payment histories
remain auditable after catalog data changes.

```mermaid
erDiagram
    USERS {
        uniqueidentifier Id PK
        nvarchar UserName UK
        nvarchar Email UK
        int TokenVersion
        rowversion RowVersion
    }
    ROLES {
        uniqueidentifier Id PK
        nvarchar Name UK
    }
    PERMISSIONS {
        uniqueidentifier Id PK
        nvarchar Name UK
    }
    USER_ROLES {
        uniqueidentifier UserId PK,FK
        uniqueidentifier RoleId FK
    }
    ROLE_PERMISSIONS {
        uniqueidentifier RoleId PK,FK
        uniqueidentifier PermissionId PK,FK
    }
    REFRESH_TOKENS {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        uniqueidentifier FamilyId
        nvarchar TokenHash UK
        datetime2 ExpiresAt
    }
    PASSWORD_RESET_TOKENS {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        nvarchar TokenHash UK
        datetime2 ExpiresAt
    }
    CATEGORIES {
        uniqueidentifier Id PK
        uniqueidentifier ParentId FK
        nvarchar NormalizedName
        bit IsDeleted
    }
    PRODUCTS {
        uniqueidentifier Id PK
        uniqueidentifier CategoryId FK
        decimal Price
        int StockQuantity
        bit IsDeleted
        rowversion RowVersion
    }
    PRODUCT_IMAGES {
        uniqueidentifier Id PK
        uniqueidentifier ProductId FK
        nvarchar ImageUrl
        bit IsMain
    }
    CARTS {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK,UK
    }
    CART_ITEMS {
        uniqueidentifier Id PK
        uniqueidentifier CartId FK
        uniqueidentifier ProductId FK
        int Quantity
        decimal UnitPrice
    }
    ORDERS {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        nvarchar OrderNumber UK
        nvarchar IdempotencyKey
        int Status
        decimal TotalAmount
        datetime2 ExpiresAt
        rowversion RowVersion
    }
    ORDER_DETAILS {
        uniqueidentifier Id PK
        uniqueidentifier OrderId FK
        uniqueidentifier ProductId FK
        nvarchar ProductNameSnapshot
        decimal UnitPrice
        int Quantity
    }
    ORDER_STATUS_HISTORIES {
        uniqueidentifier Id PK
        uniqueidentifier OrderId FK
        uniqueidentifier ChangedByUserId FK
        int FromStatus
        int ToStatus
        datetime2 CreatedAt
    }
    PAYMENTS {
        uniqueidentifier Id PK
        uniqueidentifier OrderId FK,UK
        int Method
        int Status
        decimal Amount
        datetime2 PaidAt
        rowversion RowVersion
    }
    PAYMENT_STATUS_HISTORIES {
        uniqueidentifier Id PK
        uniqueidentifier PaymentId FK
        int FromStatus
        int ToStatus
        int Source
        datetime2 OccurredAt
    }
    PAYMENT_WEBHOOK_EVENTS {
        uniqueidentifier Id PK
        uniqueidentifier PaymentId FK
        nvarchar Provider
        nvarchar EventId
        nvarchar PayloadHash
    }
    INVENTORY_TRANSACTIONS {
        uniqueidentifier Id PK
        uniqueidentifier ProductId FK
        uniqueidentifier OrderId FK
        int QuantityChange
        int BalanceAfter
        int Type
    }
    OUTBOX_MESSAGES {
        uniqueidentifier Id PK
        nvarchar Type
        datetime2 OccurredAt
        datetime2 ProcessedAt
        datetime2 DeadLetteredAt
    }
    AUDIT_EVENTS {
        uniqueidentifier Id PK
        uniqueidentifier ActorUserId
        nvarchar Action
        nvarchar EntityType
        datetime2 OccurredAt
    }

    USERS ||--|| CARTS : owns
    USERS ||--o{ USER_ROLES : has
    ROLES ||--o{ USER_ROLES : assigned
    ROLES ||--o{ ROLE_PERMISSIONS : grants
    PERMISSIONS ||--o{ ROLE_PERMISSIONS : contains
    USERS ||--o{ REFRESH_TOKENS : sessions
    USERS ||--o{ PASSWORD_RESET_TOKENS : resets
    USERS ||--o{ ORDERS : places
    CATEGORIES o|--o{ CATEGORIES : parent
    CATEGORIES ||--o{ PRODUCTS : groups
    PRODUCTS ||--o{ PRODUCT_IMAGES : has
    CARTS ||--o{ CART_ITEMS : contains
    PRODUCTS ||--o{ CART_ITEMS : selected
    ORDERS ||--|{ ORDER_DETAILS : snapshots
    PRODUCTS ||--o{ ORDER_DETAILS : references
    ORDERS ||--o{ ORDER_STATUS_HISTORIES : records
    ORDERS ||--|| PAYMENTS : payment
    PAYMENTS ||--o{ PAYMENT_STATUS_HISTORIES : records
    PAYMENTS ||--o{ PAYMENT_WEBHOOK_EVENTS : receives
    PRODUCTS ||--o{ INVENTORY_TRANSACTIONS : ledger
    ORDERS o|--o{ INVENTORY_TRANSACTIONS : causes
```

Filtered unique indexes enforce active category names and one main image per product. Additional
unique constraints protect cart lines, order idempotency, webhook event identity and inventory
movement identity. See `AppDbContext` and the EF migrations for the complete physical schema.
