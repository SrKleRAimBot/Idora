//! idora's `sqlx` feature: `Id` and `TypedId` bound as parameters and decoded from text columns,
//! against an in-memory SQLite database. PostgreSQL is checked at compile time.

use std::time::Duration;

use idora::{Entity, Id, Idora, Strategy, TypedId};
use sqlx::{Connection, FromRow, Postgres, Sqlite, SqliteConnection, Type};

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

#[derive(Debug, FromRow)]
struct UserRow {
    id: TypedId<User>,
    referrer: Option<Id>,
    name: String,
}

async fn database() -> SqliteConnection {
    let mut db = SqliteConnection::connect("sqlite::memory:").await.unwrap();
    sqlx::query("CREATE TABLE users (id CHAR(30) PRIMARY KEY, referrer TEXT, name TEXT NOT NULL)")
        .execute(&mut db)
        .await
        .unwrap();
    db
}

fn ids() -> Idora {
    Idora::builder().register_entity::<User>().unwrap().build()
}

#[tokio::test]
async fn ids_bind_and_decode_as_text() {
    let mut db = database().await;
    let ids = ids();
    let alice: TypedId<User> = ids.generate_id::<User>().unwrap().try_into().unwrap();
    let bob = ids.generate_id::<User>().unwrap();

    sqlx::query("INSERT INTO users (id, referrer, name) VALUES (?, ?, ?), (?, ?, ?)")
        .bind(alice)
        .bind(None::<Id>)
        .bind("Alice")
        .bind(bob)
        .bind(Some(alice.into_id()))
        .bind("Bob")
        .execute(&mut db)
        .await
        .unwrap();

    let rows: Vec<UserRow> = sqlx::query_as("SELECT id, referrer, name FROM users ORDER BY id")
        .fetch_all(&mut db)
        .await
        .unwrap();
    assert_eq!(rows.len(), 2);
    assert_eq!(
        (rows[0].id, rows[0].referrer, rows[0].name.as_str()),
        (alice, None, "Alice")
    );
    assert_eq!(
        (rows[1].id.into_id(), rows[1].referrer),
        (bob, Some(alice.into_id()))
    );

    // Stored as exactly the identifier's text.
    let text: String = sqlx::query_scalar("SELECT id FROM users WHERE name = 'Bob'")
        .fetch_one(&mut db)
        .await
        .unwrap();
    assert_eq!(text, bob.as_str());
    let found: Id = sqlx::query_scalar("SELECT id FROM users WHERE id = ?")
        .bind(bob)
        .fetch_one(&mut db)
        .await
        .unwrap();
    assert_eq!(found, bob);
}

#[tokio::test]
async fn malformed_text_in_a_column_is_a_decode_error_that_says_why() {
    let mut db = database().await;
    sqlx::query("INSERT INTO users (id, name) VALUES ('usr_not-an-id', 'Mallory')")
        .execute(&mut db)
        .await
        .unwrap();
    let error = sqlx::query_scalar::<_, Id>("SELECT id FROM users")
        .fetch_one(&mut db)
        .await
        .unwrap_err();
    assert!(
        error.to_string().contains("does not end in a valid value"),
        "{error}"
    );
}

#[tokio::test]
async fn a_typed_column_rejects_another_entitys_identifier() {
    let mut db = database().await;
    sqlx::query("INSERT INTO users (id, name) VALUES ('ord_01k226e0g1zzqdvk5vnacrgxv6', 'Order')")
        .execute(&mut db)
        .await
        .unwrap();
    assert!(
        sqlx::query_scalar::<_, Id>("SELECT id FROM users")
            .fetch_one(&mut db)
            .await
            .is_ok()
    );
    let error = sqlx::query_scalar::<_, TypedId<User>>("SELECT id FROM users")
        .fetch_one(&mut db)
        .await
        .unwrap_err();
    assert!(
        error.to_string().contains("the prefix 'usr' was expected"),
        "{error}"
    );
}

#[tokio::test]
async fn range_bounds_select_a_time_window_and_order_by_id_is_creation_order() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let mut db = database().await;
        let ids = Idora::builder()
            .strategy(strategy)
            .register_entity::<User>()
            .unwrap()
            .build();

        let mut minted = Vec::new();
        for batch in 0..3 {
            for i in 0..5 {
                let id = ids.generate_id::<User>().unwrap();
                sqlx::query("INSERT INTO users (id, name) VALUES (?, ?)")
                    .bind(id)
                    .bind(format!("{batch}-{i}"))
                    .execute(&mut db)
                    .await
                    .unwrap();
                minted.push((batch, id));
            }
            std::thread::sleep(Duration::from_millis(3));
        }

        // The middle batch, by time alone, without parsing a row.
        let middle: Vec<Id> = minted
            .iter()
            .filter(|(b, _)| *b == 1)
            .map(|(_, id)| *id)
            .collect();
        let lower = Id::lower_bound("usr", middle[0].created_at(), strategy).unwrap();
        let upper = Id::upper_bound("usr", middle[4].created_at(), strategy).unwrap();
        let window: Vec<Id> =
            sqlx::query_scalar("SELECT id FROM users WHERE id >= ? AND id <= ? ORDER BY id")
                .bind(lower)
                .bind(upper)
                .fetch_all(&mut db)
                .await
                .unwrap();
        if strategy == Strategy::Ulid {
            // One thread, so ULIDs are strictly increasing and the order is exact.
            assert_eq!(window, middle);
        } else {
            let mut sorted = middle.clone();
            sorted.sort_by(|a, b| a.as_str().cmp(b.as_str()));
            assert_eq!(window, sorted);
        }
    }
}

fn assert_bindable<DB: sqlx::Database>()
where
    Id: Type<DB> + for<'q> sqlx::Encode<'q, DB> + for<'r> sqlx::Decode<'r, DB>,
    TypedId<User>: Type<DB> + for<'q> sqlx::Encode<'q, DB> + for<'r> sqlx::Decode<'r, DB>,
{
}

#[test]
fn every_driver_sees_an_id_as_its_string_type() {
    assert_bindable::<Sqlite>();
    assert_bindable::<Postgres>();
    assert_eq!(
        <Id as Type<Postgres>>::type_info(),
        <String as Type<Postgres>>::type_info()
    );
    assert_eq!(
        <Id as Type<Sqlite>>::type_info(),
        <String as Type<Sqlite>>::type_info()
    );
    let varchar = <String as Type<Postgres>>::type_info();
    assert!(<TypedId<User> as Type<Postgres>>::compatible(&varchar));
}
