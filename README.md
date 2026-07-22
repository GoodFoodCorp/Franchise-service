# Franchise Service

Microservice **.NET 8** propriétaire des **restaurants (franchises)** et de leurs
**fournisseurs**. C'est lui qui définit le référentiel des restaurants : leur
identifiant est le `tenant_id` porté par les JWT et référencé par les menus, les
stocks, les commandes et les réservations.

| | |
|---|---|
| **Langage / techno** | C# / .NET 8, MediatR (CQRS), EF Core, FluentValidation, Serilog, Swashbuckle |
| **Base de données** | PostgreSQL (port hôte `5438`) |
| **Port HTTP** | `8089` |
| **Documentation API** | http://localhost:8089/docs |

---

## Architecture — Clean Architecture en couches

```
FranchiseService.slnx
src/
├── Core/
│   ├── Franchise.Domain/          # Entités (Restaurant, Supplier), erreurs typées,
│   │                              # interfaces des repositories
│   │                              # → AUCUNE dépendance externe (pas d'EF Core ici)
│   └── Franchise.Application/     # CQRS : un Command/Query + Handler par cas d'usage,
│                                  # validation FluentValidation via pipeline MediatR,
│                                  # règles d'autorisation (CurrentUser)
├── Infrastructure/
│   └── Franchise.Infrastructure/  # DbContext EF Core, repositories, migrations,
│                                  # TenantImporter (migration initiale)
└── Presentation/
    └── Franchise.Api/             # Contrôleurs fins (une ligne : mediator.Send),
                                   # validation JWT, middleware d'erreurs, Swagger
tests/
├── Franchise.Domain.Tests/        # xUnit + FluentAssertions
└── Franchise.Application.Tests/   # + Moq (isolation multi-tenant)
```

**Règle** : la logique métier vit dans `Domain` et `Application`. Les contrôleurs
ne font que construire la commande, l'envoyer et retourner le résultat.

---

## Fonctionnalités

### Restaurants
- **Liste publique des restaurants** — consommée par le storefront (sans authentification)
- **Détail d'un restaurant**
- **Création d'un restaurant** — réservée au siège (`admin`)
- **Modification d'un restaurant** — le siège sur n'importe lequel, le franchisé
  uniquement sur le sien
- Génération automatique d'un slug propre pour les URL (minuscules, sans accents)

### Fournisseurs (back-office franchisé)
- **Liste des fournisseurs** de son restaurant
- **Ajout d'un fournisseur** (nom, contact, email, téléphone)
- **Suppression d'un fournisseur**
- Isolation stricte : un franchisé ne peut ni voir ni supprimer les fournisseurs
  d'un autre restaurant (`403`)

### Migration initiale
Au premier démarrage, si la base est vide, le service **importe les restaurants
depuis `auth-service`** (où ils vivaient historiquement sous le nom de « tenants »)
**en préservant leurs identifiants** — indispensable car les commandes, stocks et
menus existants les référencent. Si `auth-service` est injoignable, un jeu de
restaurants par défaut est créé.

---

## Endpoints

| Méthode | Route | Accès |
|---|---|---|
| GET | `/api/restaurants` | public |
| GET | `/api/restaurants/{id}` | public |
| POST | `/api/restaurants` | `admin` |
| PATCH | `/api/restaurants/{id}` | `admin`, `manager` (le sien) |
| GET | `/api/franchise/suppliers` | `manager`, `admin` |
| POST | `/api/franchise/suppliers` | `manager`, `admin` |
| DELETE | `/api/franchise/suppliers/{id}` | `manager` (le sien) |
| GET | `/healthz`, `/readyz` | public (sondes) |

---

## Dépendances

> **Légende** — 🔴 indispensable (le service ne démarre pas ou ne sert à rien) ·
> 🟠 nécessaire à une fonctionnalité (le reste continue de marcher) ·
> 🟡 optionnelle (dégradation silencieuse, journalisée)

| Dépendance | Type | Conséquence si absente |
|---|---|---|
| **PostgreSQL** (`franchise-db`) | 🔴 | Le service ne démarre pas |
| **auth-service** | 🟡 | **Au tout premier démarrage uniquement**, pour importer les restaurants historiques en préservant leurs identifiants. S'il est injoignable, un jeu de restaurants par défaut est créé (avec de **nouveaux** identifiants — à éviter si des données existent déjà ailleurs). Ensuite, plus aucun appel. |

**Aucun autre appel sortant.**

### Qui dépend de ce service

| Service | Type | Conséquence si `franchise-service` est arrêté |
|---|---|---|
| `web-app` | 🔴 | La page d'accueil (liste des restaurants) est vide |
| `menu-service` | 🟡 | Le seed des menus est reporté au prochain démarrage ; les menus existants restent servis |
| `auth-service` | 🟡 | Au seed uniquement : les managers ne sont pas rattachés à leur restaurant |

Tous les autres services **référencent** les identifiants de restaurant, mais
sans jamais appeler ce service : ils fonctionnent donc normalement sans lui.

---

## Lancement

```bash
docker network create microservices-net   # une seule fois, partagé
cp .env.example .env                      # renseigner POSTGRES_PASSWORD et JWT_SECRET
docker compose up -d --build
```

⚠️ `JWT_SECRET` doit être **identique** à celui de `auth-service`.

### Variables d'environnement

| Variable | Requis | Description |
|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | oui | Base dédiée `franchise-db` |
| `JWT_SECRET` | oui | Secret HS256 partagé avec `auth-service` |
| `AUTH_SERVICE_URL` | non | Défaut `http://auth-service:8081` (import initial) |
| `ConnectionStrings__FranchiseDb` | hors Docker | Chaîne de connexion Npgsql |

---

## Tests

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test FranchiseService.slnx
```

Couvre la génération de slug, la préservation des identifiants à l'import, et
l'isolation multi-tenant des fournisseurs.

> ⚠️ **Aucune CI n'est configurée sur ce projet** — les tests doivent être lancés
> manuellement.
