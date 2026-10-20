package io.lanproof.companion

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import org.json.JSONObject

class MeasurementStore(context:Context):SQLiteOpenHelper(context,"measurements.db",null,1) {
    override fun onCreate(db:SQLiteDatabase) {
        db.execSQL("CREATE TABLE cycles (id INTEGER PRIMARY KEY AUTOINCREMENT, captured INTEGER NOT NULL, payload TEXT NOT NULL, acknowledged INTEGER NOT NULL DEFAULT 0)")
        db.execSQL("CREATE INDEX cycles_queue ON cycles(acknowledged,id)")
    }
    override fun onUpgrade(db:SQLiteDatabase,oldVersion:Int,newVersion:Int)=Unit
    @Synchronized fun append(cycle:Cycle):Long {
        check(queued()<100_000){"Очередь заполнена: подключите ПК перед продолжением записи"}
        return writableDatabase.insertOrThrow("cycles",null,ContentValues().apply{put("captured",cycle.timestampUtcMs);put("payload",cycle.json().toString())})
    }
    @Synchronized fun pending(limit:Int=30):List<Cycle> = readableDatabase.rawQuery("SELECT id,payload FROM cycles WHERE acknowledged=0 ORDER BY id LIMIT ?",arrayOf(limit.coerceIn(1,50).toString())).use {c->
        buildList {while(c.moveToNext())add(Cycle.fromJson(JSONObject(c.getString(1))).copy(sequence=c.getLong(0)))}
    }
    @Synchronized fun acknowledge(through:Long) {
        writableDatabase.execSQL("UPDATE cycles SET acknowledged=1 WHERE id<=?",arrayOf(through))
        writableDatabase.delete("cycles","acknowledged=1 AND captured<?",arrayOf((System.currentTimeMillis()-7*86_400_000L).toString()))
    }
    @Synchronized fun queued():Long=readableDatabase.rawQuery("SELECT COUNT(*) FROM cycles WHERE acknowledged=0",null).use{it.moveToFirst();it.getLong(0)}
    @Synchronized fun total():Long=readableDatabase.rawQuery("SELECT COUNT(*) FROM cycles",null).use{it.moveToFirst();it.getLong(0)}
    @Synchronized fun latest():Cycle?=readableDatabase.rawQuery("SELECT id,payload FROM cycles ORDER BY id DESC LIMIT 1",null).use {
        if(it.moveToFirst())Cycle.fromJson(JSONObject(it.getString(1))).copy(sequence=it.getLong(0)) else null
    }
    @Synchronized fun expireOldQueue(now:Long=System.currentTimeMillis(),anchor:ClockAnchor?=null):Int {
        val minimum=now-29*86_400_000L;val maximum=now+5*60_000
        val candidates=readableDatabase.rawQuery("SELECT id,payload FROM cycles WHERE acknowledged=0 AND (captured<? OR captured>?)",arrayOf(minimum.toString(),maximum.toString())).use {c->
            buildList {while(c.moveToNext())add(c.getLong(0) to Cycle.fromJson(JSONObject(c.getString(1))))}
        }
        var expired=0
        writableDatabase.beginTransaction()
        try {
            candidates.forEach{(id,original)->
                val aligned=anchor?.align(original)?:original
                if(aligned.timestampUtcMs in minimum..maximum)
                    writableDatabase.update("cycles",ContentValues().apply{put("captured",aligned.timestampUtcMs);put("payload",aligned.json().toString())},"id=?",arrayOf(id.toString()))
                else {writableDatabase.delete("cycles","id=?",arrayOf(id.toString()));expired++}
            }
            writableDatabase.setTransactionSuccessful()
        }finally{writableDatabase.endTransaction()}
        return expired
    }
}
