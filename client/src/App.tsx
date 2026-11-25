import { APITester } from "./APITester";
import "./index.css";

import logo from "./logo.svg";
import reactLogo from "./react.svg";
import {useEffect, useState} from "react";
import {Api, type User} from "../Api.ts";

const MyApi = new Api();

export function App() {

    const [users, setUsers] = useState<User[]>([])

    useEffect(()=>{
        MyApi.getUsers.userGetUsers().then(r => {
            const data = r.data;
            setUsers(data);
        })
    })


  return (
    <div className="app">
        {
        users.map(u => {
        return <div key={u.userID}> {u.userName} </div>
            })
        }
    </div>
  );
}

export default App;
